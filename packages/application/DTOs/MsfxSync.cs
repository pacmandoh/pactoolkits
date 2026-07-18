namespace PacToolkits.Application.DTOs;

public sealed record MsfxPullWindow(DateTimeOffset BeginAt, DateTimeOffset EndAt);

public sealed record MsfxPullCursorState(
    DateTimeOffset? LastSuccessBegin,
    DateTimeOffset? LastSuccessEnd,
    long? LastBatchId,
    DateTimeOffset? UpdatedAt);

public sealed record MsfxPullBatchStartResult(long BatchId);

public sealed record MsfxBillRetryRow(
    string BillCode,
    string? FromRefUserId,
    string? ToRefUserId,
    int RetryCount,
    DateTimeOffset NextRetryAt,
    string? LastError);

public sealed record MsfxMapApplyResult(int ProcessedCount, int MappedCount, int ReviewCount);

public sealed record MsfxMappingStatusSnapshot(
    int PendingCount,
    int MappedCount,
    int NeedReviewCount,
    int FailedCount,
    int TotalCount);

public sealed record MsfxBuildInject(int CreatedTasks, int TaskedCodes);

public sealed record MsfxIngestDetailResult(
    int ProcessedItems,
    int NewItems,
    int ProcessedCodes,
    int NewCodes);

public sealed record MsfxMappingBacklogDiagnostic(
    int PendingCount,
    int PendingWithDrugRawCount,
    int PendingWithSpecRawCount,
    int PendingWithRelationCount,
    int PendingWithNameNormCount,
    int PendingWithSpecNormCount);

public sealed record MsfxPullBatchRow(
    long BatchId,
    string SourceApi,
    DateTime? BeginDate,
    DateTime? EndDate,
    string Status,
    int SuccessCount,
    int FailCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? ErrMsg);

public sealed record MsfxMappingQueueRow(
    long StagingId,
    string LeafCode,
    string? ProduceBatchNo,
    string MapStatus,
    string CodeStatus,
    string? MapReasonCode,
    string? MapReasonDetail,
    string? SourceBillCode,
    string? SourceDrugNameRaw,
    string? SourceSpecRaw,
    string? SourceNameNorm,
    string? SourceSpecNorm,
    string? SourceCodeLevel1,
    string? SourceCodeLevel2,
    string? SourceCodeLevel3,
    string? SourceCodeLevel4,
    string? SourceCodeLevel5,
    string? MappedDrugId,
    string? MappedSpec,
    string? SourceBillTime,
    DateTimeOffset UpdatedAt);

public sealed record MsfxMappingQueuePage(
    IReadOnlyList<MsfxMappingQueueRow> Rows,
    int TotalCount,
    bool HasNewer,
    bool HasOlder);

public sealed record MsfxMappingBatchPreview(
    int CandidateCount,
    int EligibleCount,
    int BlockedCount);

public sealed record MsfxMappingBatchApplyResult(int AffectedCount);

public sealed record MsfxMappingBatchGroupRow(
    string SourceBillTimes,
    string SourceBillCodes,
    string SourceDrugNameRaw,
    string SourceSpecRaw,
    string SourceNameNorm,
    string SourceSpecNorm,
    int TotalCount,
    int PendingCount,
    int NeedReviewCount,
    int FailedCount);

public sealed record MsfxInjectQueueRow(
    long TaskId,
    string Status,
    string? SourceBillCode,
    string? BatchNos,
    string MappedDrugId,
    string MappedSpec,
    int TotalCodes,
    int CurrentCodeCount,
    int SuccessCodes,
    int FailedCodes,
    int RetryCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PickedAt,
    DateTimeOffset? FinishedAt,
    string? ErrMsg);

public sealed record MsfxInjectReopen(
    long TaskId,
    string Status,
    int TotalCodes);

public sealed record MsfxInjectDiscard(
    long TaskId,
    string Status,
    int TotalCodes);

public sealed record MsfxInjectRemap(
    long TaskId,
    string Status,
    int TotalCodes,
    int ResetStagingCount);

public sealed record MsfxInjectMerge(
    long TaskId,
    string Status,
    int TotalCodes,
    int MergedTaskCount);

public sealed record MsfxInjectSplit(
    int CreatedTasks,
    int TotalCodes,
    string SplitMode);

public sealed record MsfxInjectSplitCustom(
    int CreatedTasks,
    int TotalCodes,
    int BucketCount);

public sealed record MsfxInjectSplitUnitRow(
    string GroupKey,
    string ParentClusterKey,
    string DisplayClusterCode,
    string? CodeLevel1,
    string? CodeLevel2,
    string? CodeLevel3,
    string? CodeLevel4,
    string? CodeLevel5,
    string BatchNo,
    string SourceBillCodes,
    int CodeCount);

public sealed record MsfxInjectSplitCodeRow(
    string GroupKey,
    string DisplayClusterCode,
    string LeafCode,
    string? CodeLevel1,
    string? CodeLevel2,
    string? CodeLevel3,
    string? CodeLevel4,
    string? CodeLevel5,
    string BatchNo,
    string SourceBillCode);

public sealed record MsfxAutoBoardSnapshot(
    long LastBatchId,
    string LastBatchStatus,
    DateTimeOffset? LastBatchStartedAt,
    DateTimeOffset? LastBatchFinishedAt,
    int LastBatchSuccessCount,
    int LastBatchFailCount,
    int StagingNewCount,
    int StagingTaskedCount,
    int StagingInjectedCount,
    int StagingVerifiedCount,
    int StagingPooledCount,
    int StagingFailedCount,
    int StagingDuplicateCount,
    int MapPendingCount,
    int MapMappedCount,
    int MapNeedReviewCount,
    int MapFailedCount,
    int TaskNewCount,
    int TaskRunningCount,
    int TaskSuccessCount,
    int TaskFailedCount,
    int TaskCancelledCount,
    int TaskDiscardedCount);

public sealed record MsfxBillWatchRow(
    string BillCode,
    string? FromRefUserId,
    string? ToRefUserId,
    string? FromEntName,
    string? BillType,
    string? BillTime,
    string? BillUploadTime,
    string? LastSeenStatus,
    string? RawJson,
    int RetryCount,
    DateTimeOffset NextCheckAt);
