namespace PacToolkits.Application.DTOs;

/// <summary>MSFX PacApi 写命令体（库侧 Sync）</summary>
public sealed record MsfxCursorAdvanceRequest(string SourceApi, DateTimeOffset Target);

public sealed record MsfxMappingApplyRequest(int Limit);

public sealed record MsfxMappingBatchRequest(
    string? MapStatus,
    string? CodeStatus,
    string? SearchScope,
    string? Keyword,
    string? GroupSourceDrugNameRaw,
    string? GroupSourceSpecRaw,
    string? GroupSourceNameNorm,
    string? GroupSourceSpecNorm,
    string Action,
    string? DrugId,
    string? Spec);

public sealed record MsfxInjectBuildRequest(int MaxGroups);

public sealed record MsfxInjectReasonRequest(string? OperatorName, string? Reason);

public sealed record MsfxInjectMergeRequest(IReadOnlyList<long> TaskIds, string? OperatorName, string? Reason);

public sealed record MsfxInjectSplitRequest(string SplitMode, string? OperatorName, string? Reason);

public sealed record MsfxInjectSplitCustomRequest(
    IReadOnlyList<string> GroupKeys,
    IReadOnlyList<int> BucketIndexes,
    string? OperatorName,
    string? Reason);
