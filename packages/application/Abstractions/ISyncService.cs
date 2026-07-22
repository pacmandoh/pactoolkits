using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// MSFX 同步业务门面（拉窗、映射、注入任务运维）
/// </summary>
public interface ISyncService
{
    Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct);
    Task<MsfxPullCursorState> GetPullCursorAsync(string sourceApi, CancellationToken ct);
    Task<MsfxPullCursorState> AdvancePullCursorToAsync(string sourceApi, DateTimeOffset target, CancellationToken ct);
    Task<MsfxPullBatchStartResult> StartPullBatchAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, CancellationToken ct);
    Task FinishPullBatchAsync(long batchId, string status, int successCount, int failCount, string? errMsg, CancellationToken ct);
    Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct);
    Task AdvancePullCursorAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, long batchId, string batchStatus, CancellationToken ct);
    Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(string sourceApi, int limit, CancellationToken ct);
    Task UpsertBillRetryAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? lastError, CancellationToken ct);
    Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct);
    Task UpsertBillWatchAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? fromEntName, string? billType, string? billTime, string? billUploadTime, string? lastSeenStatus, string? rawJson, CancellationToken ct);
    Task<IReadOnlyList<MsfxBillWatchRow>> GetDueBillWatchesAsync(string sourceApi, int limit, CancellationToken ct);
    Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct);
    Task RescheduleBillWatchAsync(string sourceApi, string billCode, string? lastSeenStatus, string? lastError, CancellationToken ct);
    Task<long> UpsertInboundBillAsync(long batchId, string billCode, string billType, string billTime, string billUploadTime, string fromRefUserId, string fromEntName, string toRefUserId, string toUserId, string toUserName, string status, string rawJson, CancellationToken ct);
    Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(long billId, string billCode, IReadOnlyList<(string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes)> drugs, CancellationToken ct);
    Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct);
    Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct);
    Task<MsfxBuildInject> BuildInjectsAsync(int maxGroups, CancellationToken ct);
    Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct);
    Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct);
    Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(int pageSize, IReadOnlyCollection<string>? mapStatuses, string? codeStatus, string? searchScope, string? keyword, DateTimeOffset? cursorUpdatedAt, long? cursorId, bool newer, bool seekLastPage, CancellationToken ct);
    Task<IReadOnlyList<MsfxInjectQueueRow>> GetInjectQueueAsync(int limit, CancellationToken ct);
    Task<MsfxInjectReopen> ReopenInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);
    Task<MsfxInjectDiscard> DiscardInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);
    Task<MsfxInjectRemap> RemapInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);
    Task<MsfxInjectMerge> MergeInjectsAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct);
    Task<MsfxInjectSplit> SplitInjectAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct);
    Task<IReadOnlyList<MsfxInjectSplitUnitRow>> GetInjectSplitUnitsAsync(long taskId, CancellationToken ct);
    Task<IReadOnlyList<MsfxInjectSplitCodeRow>> GetInjectSplitCodeRowsAsync(long taskId, CancellationToken ct);
    Task<MsfxInjectSplitCustom> SplitInjectCustomAsync(long taskId, IReadOnlyList<string> groupKeys, IReadOnlyList<int> bucketIndexes, string? operatorName, string? reason, CancellationToken ct);
    Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, int limit, CancellationToken ct);
    Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, string? groupSourceDrugNameRaw, string? groupSourceSpecRaw, string? groupSourceNameNorm, string? groupSourceSpecNorm, string action, string? drugId, string? spec, CancellationToken ct);
    Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, string? groupSourceDrugNameRaw, string? groupSourceSpecRaw, string? groupSourceNameNorm, string? groupSourceSpecNorm, string action, string? drugId, string? spec, CancellationToken ct);
}
