namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 需敏感解锁的 MSFX 操作种类
/// </summary>
public enum SensitiveOpKind
{
    MsfxDiscard,
    MsfxMerge,
    MsfxSplit,
    MsfxMappingApply,
    MsfxReopen,
    MsfxRemap
}

/// <summary>
/// 敏感操作解锁请求（范围键、场景文案与审计字段）
/// </summary>
public sealed record SensitiveOpRequest(
    SensitiveOpKind Kind,
    string ScopeKey,
    string Scene,
    string PromptTitle,
    string PromptHint,
    string? OperatorName = null,
    string? TargetId = null,
    string? Reason = null);

/// <summary>
/// 某 scope 当前解锁态快照（过期、失败次数、冷却）
/// </summary>
public sealed record UnlockScopeSnapshot(
    bool IsUnlocked,
    DateTimeOffset ExpiresAtUtc,
    int FailedAttempts,
    DateTimeOffset CooldownUntilUtc);

/// <summary>
/// 敏感操作按 scope 解锁会话（提示、校验、锁定与状态通知）
/// </summary>
public interface ISensitiveUnlockService
{
    event Action<string>? StateChanged;

    UnlockScopeSnapshot GetSnapshot(string scopeKey);
    void Refresh(string scopeKey);
    void Lock(string scopeKey);

    Task<bool> RequestUnlockAsync(SensitiveOpRequest request, CancellationToken ct = default);

    Task<bool> RequireUnlockAsync(
        string scopeKey,
        string scene,
        string promptTitle,
        string promptHint,
        CancellationToken ct = default);
}
