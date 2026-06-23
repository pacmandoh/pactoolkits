namespace PacToolkits.Application.Abstractions;

public enum SensitiveOpKind
{
    MsfxDiscard,
    MsfxMerge,
    MsfxSplit,
    MsfxMappingApply,
    MsfxReopen,
    MsfxRemap
}

public sealed record SensitiveOpRequest(
    SensitiveOpKind Kind,
    string ScopeKey,
    string Scene,
    string PromptTitle,
    string PromptHint,
    string? OperatorName = null,
    string? TargetId = null,
    string? Reason = null,
    bool NotifySuccess = true);

public sealed record UnlockScopeSnapshot(
    bool IsUnlocked,
    DateTimeOffset ExpiresAtUtc,
    int FailedAttempts,
    DateTimeOffset CooldownUntilUtc);

public interface ISensitiveUnlockService
{
    event Action<string>? StateChanged;

    UnlockScopeSnapshot GetSnapshot(string scopeKey);
    void Refresh(string scopeKey);
    void Lock(string scopeKey);

    Task<bool> RequestUnlockAsync(SensitiveOpRequest request, CancellationToken ct = default);

    Task<bool> EnsureUnlockedAsync(
        string scopeKey,
        string scene,
        string promptTitle,
        string promptHint,
        bool notifySuccess = true,
        CancellationToken ct = default);
}
