using System;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public sealed record UnlockScopeSnapshot(
    bool IsUnlocked,
    DateTimeOffset ExpiresAtUtc,
    int FailedAttempts,
    DateTimeOffset CooldownUntilUtc);

public interface ISensitiveOperationUnlockService
{
    event Action<string>? StateChanged;

    UnlockScopeSnapshot GetSnapshot(string scopeKey);
    void Refresh(string scopeKey);
    void Lock(string scopeKey);
    Task<bool> EnsureUnlockedAsync(
        string scopeKey,
        string scene,
        string promptTitle,
        string promptHint,
        CancellationToken ct = default);
}

public sealed class SensitiveOperationUnlockService : ISensitiveOperationUnlockService
{
    private static readonly TimeSpan UnlockSessionDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan UnlockCooldownDuration = TimeSpan.FromMinutes(1);
    private const int UnlockFailedAttemptThreshold = 5;

    private sealed class ScopeState
    {
        public bool IsUnlocked { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.MinValue;
        public int FailedAttempts { get; set; }
        public DateTimeOffset CooldownUntilUtc { get; set; } = DateTimeOffset.MinValue;
        public bool IsPromptActive { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, ScopeState> _states = new(StringComparer.Ordinal);
    private readonly IDbConfigService _dbConfig;
    private readonly IDialogService _dialog;
    private readonly IToastService _toast;
    public event Action<string>? StateChanged;

    public SensitiveOperationUnlockService(
        IDbConfigService dbConfig,
        IDialogService dialog,
        IToastService toast)
    {
        _dbConfig = dbConfig;
        _dialog = dialog;
        _toast = toast;
    }

    public UnlockScopeSnapshot GetSnapshot(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            return ToSnapshot(state);
        }
    }

    public void Refresh(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            if (state.CooldownUntilUtc != DateTimeOffset.MinValue && state.CooldownUntilUtc <= now)
            {
                state.CooldownUntilUtc = DateTimeOffset.MinValue;
                changed = true;
            }

            if (state.IsUnlocked && state.ExpiresAtUtc <= now)
            {
                state.IsUnlocked = false;
                state.ExpiresAtUtc = DateTimeOffset.MinValue;
                changed = true;
            }
        }

        if (changed)
            RaiseStateChanged(key);
    }

    public void Lock(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        var changed = false;
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            changed = state.IsUnlocked || state.ExpiresAtUtc != DateTimeOffset.MinValue;
            state.IsUnlocked = false;
            state.ExpiresAtUtc = DateTimeOffset.MinValue;
        }

        if (changed)
            RaiseStateChanged(key);
    }

    public async Task<bool> EnsureUnlockedAsync(
        string scopeKey,
        string scene,
        string promptTitle,
        string promptHint,
        CancellationToken ct = default)
    {
        var key = NormalizeScope(scopeKey);
        ScopeState state;
        lock (_gate)
        {
            state = GetOrCreateState(key);
            if (state.IsPromptActive)
                return false;
        }

        Refresh(key);

        lock (_gate)
        {
            state = GetOrCreateState(key);
            if (state.IsUnlocked)
                return true;
        }

        var expectedPassword = NormalizeInput(_dbConfig.Current.Password);
        if (string.IsNullOrWhiteSpace(expectedPassword))
        {
            _toast.Error(scene, "当前未配置数据库密码，无法执行该操作");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        DateTimeOffset cooldownUntil;
        lock (_gate)
        {
            state = GetOrCreateState(key);
            cooldownUntil = state.CooldownUntilUtc;
        }

        if (cooldownUntil > now)
        {
            var left = cooldownUntil - now;
            _toast.Warn(scene, $"验证冷却中，请在 {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} 秒后重试");
            return false;
        }

        string? input;
        try
        {
            lock (_gate)
            {
                state = GetOrCreateState(key);
                if (state.IsPromptActive)
                    return false;
                state.IsPromptActive = true;
            }

            int failed;
            lock (_gate)
            {
                state = GetOrCreateState(key);
                failed = state.FailedAttempts;
            }

            var suffix = failed <= 0 ? promptHint : $"{promptHint}\n（已失败 {failed} 次）";
            input = NormalizeInput(await _dialog.PromptInventoryUnlockPassword(promptTitle, suffix));
        }
        finally
        {
            lock (_gate)
            {
                state = GetOrCreateState(key);
                state.IsPromptActive = false;
            }
        }

        if (ct.IsCancellationRequested)
            return false;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        if (!string.Equals(input, expectedPassword, StringComparison.Ordinal))
        {
            bool lockout;
            int remaining;
            var changed = false;
            lock (_gate)
            {
                state = GetOrCreateState(key);
                state.FailedAttempts++;
                changed = true;
                lockout = state.FailedAttempts >= UnlockFailedAttemptThreshold;
                if (lockout)
                {
                    state.CooldownUntilUtc = DateTimeOffset.UtcNow + UnlockCooldownDuration;
                    state.FailedAttempts = 0;
                    remaining = 0;
                }
                else
                {
                    remaining = UnlockFailedAttemptThreshold - state.FailedAttempts;
                }
            }

            if (lockout)
                _toast.Error(scene, $"密码连续错误过多，已锁定 {UnlockCooldownDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} 秒");
            else
                _toast.Error(scene, $"密码错误，还可重试 {remaining} 次");

            if (changed)
                RaiseStateChanged(key);
            return false;
        }

        lock (_gate)
        {
            state = GetOrCreateState(key);
            state.IsUnlocked = true;
            state.FailedAttempts = 0;
            state.CooldownUntilUtc = DateTimeOffset.MinValue;
            state.ExpiresAtUtc = DateTimeOffset.UtcNow + UnlockSessionDuration;
        }

        _toast.Success(scene, "验证通过，已解锁敏感操作");
        RaiseStateChanged(key);
        return true;
    }

    private void RaiseStateChanged(string scopeKey)
    {
        var handler = StateChanged;
        if (handler is null)
            return;

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<string>)subscriber).Invoke(scopeKey);
            }
            catch
            {
                // Keep unlock workflow robust even if one UI subscriber throws.
            }
        }
    }

    private ScopeState GetOrCreateState(string key)
    {
        if (_states.TryGetValue(key, out var state))
            return state;

        state = new ScopeState();
        _states[key] = state;
        return state;
    }

    private static UnlockScopeSnapshot ToSnapshot(ScopeState state)
        => new(
            state.IsUnlocked,
            state.ExpiresAtUtc,
            state.FailedAttempts,
            state.CooldownUntilUtc);

    private static string NormalizeScope(string? scopeKey)
        => string.IsNullOrWhiteSpace(scopeKey) ? "default" : scopeKey.Trim();

    private static string? NormalizeInput(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
