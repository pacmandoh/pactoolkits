using System.Globalization;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

/// <summary>
/// 按操作范围管理敏感操作的解锁期限、失败冷却和提示互斥
/// </summary>
public sealed class SensitiveUnlockSession
{
    public enum PromptStatus
    {
        Granted,
        Started,
        Active,
        CoolingDown
    }

    private static readonly TimeSpan DefaultSessionDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultCooldownDuration = TimeSpan.FromMinutes(1);
    private const int DefaultFailedAttemptThreshold = 5;

    private sealed class ScopeState
    {
        public bool IsUnlocked { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.MinValue;
        public int FailedAttempts { get; set; }
        public DateTimeOffset CooldownUntilUtc { get; set; } = DateTimeOffset.MinValue;
        public bool IsPromptActive { get; set; }
    }

    /// <summary>
    /// 敏感操作范围的访问判定结果，包括提示是否已占用
    /// </summary>
    public readonly record struct Access(string ScopeKey, bool IsGranted, bool IsPromptActive, bool StateChanged);

    public readonly record struct Prompt(
        string ScopeKey,
        PromptStatus Status,
        int FailedAttempts,
        DateTimeOffset CooldownUntilUtc,
        bool StateChanged);

    public readonly record struct Validation(
        string ScopeKey,
        bool IsSuccess,
        bool StateChanged,
        string? Error);

    private readonly object _gate = new();
    private readonly Dictionary<string, ScopeState> _states = new(StringComparer.Ordinal);
    private readonly TimeSpan _sessionDuration;
    private readonly TimeSpan _cooldownDuration;
    private readonly int _failedAttemptThreshold;

    public SensitiveUnlockSession()
        : this(DefaultSessionDuration, DefaultCooldownDuration, DefaultFailedAttemptThreshold)
    {
    }

    internal SensitiveUnlockSession(
        TimeSpan sessionDuration,
        TimeSpan cooldownDuration,
        int failedAttemptThreshold)
    {
        _sessionDuration = sessionDuration;
        _cooldownDuration = cooldownDuration;
        _failedAttemptThreshold = failedAttemptThreshold;
    }

    public UnlockScopeSnapshot GetSnapshot(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        lock (_gate)
        {
            return ToSnapshot(GetOrCreateState(key));
        }
    }

    public Access CheckAccess(string scopeKey, DateTimeOffset now)
    {
        var key = NormalizeScope(scopeKey);
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            var changed = Refresh(state, now);
            if (state.IsUnlocked)
            {
                state.ExpiresAtUtc = now + _sessionDuration;
                return new Access(key, IsGranted: true, IsPromptActive: false, changed);
            }

            return new Access(key, IsGranted: false, state.IsPromptActive, changed);
        }
    }

    public Prompt BeginPrompt(string scopeKey, DateTimeOffset now)
    {
        var key = NormalizeScope(scopeKey);
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            var changed = Refresh(state, now);
            // 已解锁范围仅延长有效期，避免重复显示口令框
            if (state.IsUnlocked)
            {
                state.ExpiresAtUtc = now + _sessionDuration;
                return new Prompt(
                    key,
                    PromptStatus.Granted,
                    state.FailedAttempts,
                    state.CooldownUntilUtc,
                    changed);
            }

            // 同一操作范围只允许一个提示，避免对话框重叠
            if (state.IsPromptActive)
            {
                return new Prompt(
                    key,
                    PromptStatus.Active,
                    state.FailedAttempts,
                    state.CooldownUntilUtc,
                    changed);
            }

            if (state.CooldownUntilUtc > now)
            {
                return new Prompt(
                    key,
                    PromptStatus.CoolingDown,
                    state.FailedAttempts,
                    state.CooldownUntilUtc,
                    changed);
            }

            state.IsPromptActive = true;
            return new Prompt(
                key,
                PromptStatus.Started,
                state.FailedAttempts,
                state.CooldownUntilUtc,
                changed);
        }
    }

    public void EndPrompt(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        lock (_gate)
        {
            GetOrCreateState(key).IsPromptActive = false;
        }
    }

    public Validation Validate(
        string scopeKey,
        string? input,
        string expected,
        DateTimeOffset now)
    {
        var key = NormalizeScope(scopeKey);
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            if (state.CooldownUntilUtc > now)
            {
                return new Validation(key, IsSuccess: false, StateChanged: false, CooldownError(state, now));
            }

            var normalized = NormalizeInput(input);
            if (normalized is null)
            {
                return new Validation(key, IsSuccess: false, StateChanged: false, "请输入数据库密码");
            }

            if (!string.Equals(normalized, expected, StringComparison.Ordinal))
            {
                state.FailedAttempts++;
                if (state.FailedAttempts >= _failedAttemptThreshold)
                {
                    state.CooldownUntilUtc = now + _cooldownDuration;
                    state.FailedAttempts = 0;
                    return new Validation(
                        key,
                        IsSuccess: false,
                        StateChanged: true,
                        $"密码连续错误过多，已锁定 {_cooldownDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture)} 秒");
                }

                var remaining = _failedAttemptThreshold - state.FailedAttempts;
                return new Validation(key, IsSuccess: false, StateChanged: true, $"密码错误，还可重试 {remaining} 次");
            }

            state.IsUnlocked = true;
            state.FailedAttempts = 0;
            state.CooldownUntilUtc = DateTimeOffset.MinValue;
            state.ExpiresAtUtc = now + _sessionDuration;
            return new Validation(key, IsSuccess: true, StateChanged: true, Error: null);
        }
    }

    public bool Refresh(string scopeKey, DateTimeOffset now)
    {
        var key = NormalizeScope(scopeKey);
        lock (_gate)
        {
            return Refresh(GetOrCreateState(key), now);
        }
    }

    public bool Lock(string scopeKey)
    {
        var key = NormalizeScope(scopeKey);
        lock (_gate)
        {
            var state = GetOrCreateState(key);
            var changed = state.IsUnlocked || state.ExpiresAtUtc != DateTimeOffset.MinValue;
            state.IsUnlocked = false;
            state.ExpiresAtUtc = DateTimeOffset.MinValue;
            return changed;
        }
    }

    public static string NormalizeScope(string? scopeKey)
        => string.IsNullOrWhiteSpace(scopeKey) ? "default" : scopeKey.Trim();

    private static bool Refresh(ScopeState state, DateTimeOffset now)
    {
        var changed = false;
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

        return changed;
    }

    private static string CooldownError(ScopeState state, DateTimeOffset now)
    {
        var left = state.CooldownUntilUtc - now;
        return $"验证冷却中，请在 {Math.Max(1, (int)Math.Ceiling(left.TotalSeconds))} 秒后重试";
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

    private static string? NormalizeInput(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
