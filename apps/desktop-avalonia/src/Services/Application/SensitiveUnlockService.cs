using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public sealed class SensitiveUnlockService : ISensitiveUnlockService
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

    public SensitiveUnlockService(
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
        {
            RaiseStateChanged(key);
        }
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
        {
            RaiseStateChanged(key);
        }
    }

    public async Task<bool> RequireUnlockAsync(
        string scopeKey,
        string scene,
        string promptTitle,
        string promptHint,
        bool notifySuccess = true,
        CancellationToken ct = default)
    {
        var key = NormalizeScope(scopeKey);
        Refresh(key);

        lock (_gate)
        {
            var state = GetOrCreateState(key);
            if (state.IsUnlocked)
            {
                // Sliding session: each gated action extends the unlock window.
                state.ExpiresAtUtc = DateTimeOffset.UtcNow + UnlockSessionDuration;
                return true;
            }

            if (state.IsPromptActive)
            {
                // Concurrent callers must not stack password dialogs.
                return false;
            }
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
            var state = GetOrCreateState(key);
            cooldownUntil = state.CooldownUntilUtc;
        }

        if (cooldownUntil > now)
        {
            var left = cooldownUntil - now;
            _toast.Warn(scene, $"验证冷却中，请在 {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} 秒后重试");
            return false;
        }

        lock (_gate)
        {
            var state = GetOrCreateState(key);
            state.IsPromptActive = true;
        }

        string? input;
        try
        {
            int failed;
            lock (_gate)
            {
                var state = GetOrCreateState(key);
                failed = state.FailedAttempts;
            }

            var suffix = failed <= 0 ? promptHint : $"{promptHint}\n（已失败 {failed} 次）";
            input = await _dialog.PromptUnlockPassword(
                promptTitle,
                suffix,
                password => VerifyPassword(key, password, expectedPassword)).ConfigureAwait(true);
        }
        finally
        {
            lock (_gate)
            {
                var state = GetOrCreateState(key);
                state.IsPromptActive = false;
            }
        }

        if (ct.IsCancellationRequested || string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        if (notifySuccess)
        {
            _toast.Success(scene, "验证通过，已解锁敏感操作");
        }

        RaiseStateChanged(key);
        return true;
    }

    public Task<bool> RequestUnlockAsync(SensitiveOpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RequireUnlockAsync(
            request.ScopeKey,
            request.Scene,
            request.PromptTitle,
            request.PromptHint,
            request.NotifySuccess,
            ct);
    }

    private string? VerifyPassword(string key, string password, string expectedPassword)
    {
        var cooldownError = GetCooldownError(key);
        if (cooldownError is not null)
        {
            return cooldownError;
        }

        var normalized = NormalizeInput(password);
        if (normalized is null)
        {
            return "请输入数据库密码";
        }

        if (!string.Equals(normalized, expectedPassword, StringComparison.Ordinal))
        {
            return RecordFailure(key);
        }

        RecordSuccess(key);
        return null;
    }

    private string? GetCooldownError(string key)
    {
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            if (state.CooldownUntilUtc <= DateTimeOffset.UtcNow)
            {
                return null;
            }

            var left = state.CooldownUntilUtc - DateTimeOffset.UtcNow;
            return $"验证冷却中，请在 {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} 秒后重试";
        }
    }

    private string RecordFailure(string key)
    {
        bool lockout;
        int remaining;
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            state.FailedAttempts++;
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

        RaiseStateChanged(key);

        return lockout
            ? $"密码连续错误过多，已锁定 {UnlockCooldownDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} 秒"
            : $"密码错误，还可重试 {remaining} 次";
    }

    private void RecordSuccess(string key)
    {
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            state.IsUnlocked = true;
            state.FailedAttempts = 0;
            state.CooldownUntilUtc = DateTimeOffset.MinValue;
            state.ExpiresAtUtc = DateTimeOffset.UtcNow + UnlockSessionDuration;
        }
    }

    private void RaiseStateChanged(string scopeKey)
    {
        var handler = StateChanged;
        if (handler is null)
        {
            return;
        }

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
        {
            return state;
        }

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
