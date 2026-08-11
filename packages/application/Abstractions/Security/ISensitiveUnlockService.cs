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
/// 指定操作范围的解锁状态、到期时间、失败次数和冷却时间
/// </summary>
public sealed record UnlockScopeSnapshot(
    bool IsUnlocked,
    DateTimeOffset ExpiresAtUtc,
    int FailedAttempts,
    DateTimeOffset CooldownUntilUtc);

/// <summary>
/// 按操作范围管理敏感操作的提示、校验、锁定和状态通知
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
