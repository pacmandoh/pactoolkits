namespace PacToolkits.Application.DTOs;

/// <summary>MSFX PacAPI 写命令体（库侧 Sync）</summary>
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

public sealed record MsfxRunLockRequest(string SourceApi);

public sealed record MsfxRunLockResult(bool Acquired, Guid? LockId);

public sealed record MsfxRunLockReleaseRequest(string SourceApi, Guid LockId);

public sealed record MsfxCountResult(int Count);

public sealed record MsfxFailInterruptedRequest(string SourceApi, string Error);

public sealed record MsfxStartBatchRequest(string SourceApi, DateTimeOffset BeginAt, DateTimeOffset EndAt);

public sealed record MsfxFinishBatchRequest(
    long BatchId,
    string Status,
    int SuccessCount,
    int FailCount,
    string? ErrMsg);

public sealed record MsfxBatchRequestIdRequest(long BatchId, string? RequestId);

public sealed record MsfxAdvanceWindowRequest(
    string SourceApi,
    DateTimeOffset BeginAt,
    DateTimeOffset EndAt,
    long BatchId,
    string BatchStatus);

public sealed record MsfxUpsertRetryRequest(
    string SourceApi,
    string BillCode,
    string? FromRefUserId,
    string? ToRefUserId,
    string? LastError);

public sealed record MsfxBillCodeRequest(string SourceApi, string BillCode);

public sealed record MsfxUpsertWatchRequest(
    string SourceApi,
    string BillCode,
    string? FromRefUserId,
    string? ToRefUserId,
    string? FromEntName,
    string? BillType,
    string? BillTime,
    string? BillUploadTime,
    string? LastSeenStatus,
    string? RawJson);

public sealed record MsfxRescheduleWatchRequest(
    string SourceApi,
    string BillCode,
    string? LastSeenStatus,
    string? LastError);

public sealed record MsfxUpsertBillRequest(
    long BatchId,
    string BillCode,
    string BillType,
    string BillTime,
    string BillUploadTime,
    string FromRefUserId,
    string FromEntName,
    string ToRefUserId,
    string Status,
    string RawJson);

public sealed record MsfxUpsertBillResult(long BillId);

public sealed record MsfxIngestCodeDto(
    string Code,
    string CodeLevel,
    string? Level1Code,
    string? Level2Code,
    string? Level3Code,
    string? Level4Code,
    string? Level5Code);

public sealed record MsfxIngestDrugDto(
    string DrugName,
    string PackageSpec,
    string PrepnSpec,
    string BatchNo,
    IReadOnlyList<MsfxIngestCodeDto> Codes);

public sealed record MsfxIngestRequest(
    long BillId,
    string BillCode,
    IReadOnlyList<MsfxIngestDrugDto> Drugs);
