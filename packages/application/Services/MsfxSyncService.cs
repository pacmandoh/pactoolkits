using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

public interface IMsfxSyncService : IMsfxSyncRepo;

public sealed class MsfxSyncService : IMsfxSyncService
{
    private readonly IMsfxSyncRepo _repo;

    public MsfxSyncService(IMsfxSyncRepo repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct)
        => _repo.GetPullWindowAsync(sourceApi, ct);

    public Task<MsfxPullBatchStartResult> StartPullBatchAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        CancellationToken ct)
        => _repo.StartPullBatchAsync(sourceApi, beginAt, endAt, ct);

    public Task FinishPullBatchAsync(
        long batchId,
        string status,
        int successCount,
        int failCount,
        string? errMsg,
        CancellationToken ct)
        => _repo.FinishPullBatchAsync(batchId, status, successCount, failCount, errMsg, ct);

    public Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct)
        => _repo.UpdatePullBatchRequestIdAsync(batchId, requestId, ct);

    public Task AdvancePullCursorAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        long batchId,
        string batchStatus,
        CancellationToken ct)
        => _repo.AdvancePullCursorAsync(sourceApi, beginAt, endAt, batchId, batchStatus, ct);

    public Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(string sourceApi, int limit, CancellationToken ct)
        => _repo.GetDueBillRetriesAsync(sourceApi, limit, ct);

    public Task UpsertBillRetryAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? lastError,
        CancellationToken ct)
        => _repo.UpsertBillRetryAsync(sourceApi, billCode, fromRefUserId, toRefUserId, lastError, ct);

    public Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct)
        => _repo.MarkBillRetrySucceededAsync(sourceApi, billCode, ct);

    public Task UpsertBillWatchAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? fromEntName,
        string? billType,
        string? billTime,
        string? billUploadTime,
        string? lastSeenStatus,
        string? rawJson,
        CancellationToken ct)
        => _repo.UpsertBillWatchAsync(
            sourceApi,
            billCode,
            fromRefUserId,
            toRefUserId,
            fromEntName,
            billType,
            billTime,
            billUploadTime,
            lastSeenStatus,
            rawJson,
            ct);

    public Task<IReadOnlyList<MsfxBillWatchRow>> GetDueBillWatchesAsync(string sourceApi, int limit, CancellationToken ct)
        => _repo.GetDueBillWatchesAsync(sourceApi, limit, ct);

    public Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct)
        => _repo.MarkBillWatchResolvedAsync(sourceApi, billCode, ct);

    public Task RescheduleBillWatchAsync(
        string sourceApi,
        string billCode,
        string? lastSeenStatus,
        string? lastError,
        CancellationToken ct)
        => _repo.RescheduleBillWatchAsync(sourceApi, billCode, lastSeenStatus, lastError, ct);

    public Task<long> UpsertInboundBillAsync(
        long batchId,
        string billCode,
        string billType,
        string billTime,
        string billUploadTime,
        string fromRefUserId,
        string fromEntName,
        string toRefUserId,
        string toUserId,
        string toUserName,
        string status,
        string rawJson,
        CancellationToken ct)
        => _repo.UpsertInboundBillAsync(
            batchId,
            billCode,
            billType,
            billTime,
            billUploadTime,
            fromRefUserId,
            fromEntName,
            toRefUserId,
            toUserId,
            toUserName,
            status,
            rawJson,
            ct);

    public Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(
        long billId,
        string billCode,
        IReadOnlyList<(string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes)> drugs,
        CancellationToken ct)
        => _repo.IngestUpoutDetailAsync(billId, billCode, drugs, ct);

    public Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct)
        => _repo.ApplyMappingAsync(limit, ct);

    public Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct)
        => _repo.GetMappingStatusSnapshotAsync(ct);

    public Task<MsfxMappingBacklogDiagnostic> GetMappingBacklogDiagnosticAsync(CancellationToken ct)
        => _repo.GetMappingBacklogDiagnosticAsync(ct);

    public Task<MsfxBuildTaskResult> BuildInjectTasksAsync(int maxGroups, CancellationToken ct)
        => _repo.BuildInjectTasksAsync(maxGroups, ct);

    public Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct)
        => _repo.GetAutoBoardSnapshotAsync(ct);

    public Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct)
        => _repo.GetRecentPullBatchesAsync(limit, ct);

    public Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
        int pageSize,
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        DateTimeOffset? cursorUpdatedAt,
        long? cursorId,
        bool newer,
        CancellationToken ct)
        => _repo.GetMappingQueuePageAsync(
            pageSize,
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            cursorUpdatedAt,
            cursorId,
            newer,
            ct);

    public Task<IReadOnlyList<MsfxInjectTaskQueueRow>> GetInjectTaskQueueAsync(int limit, CancellationToken ct)
        => _repo.GetInjectTaskQueueAsync(limit, ct);

    public Task<MsfxReopenInjectTaskResult> ReopenInjectTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
        => _repo.ReopenInjectTaskAsync(taskId, operatorName, reason, ct);

    public Task<MsfxDiscardInjectTaskResult> DiscardInjectTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
        => _repo.DiscardInjectTaskAsync(taskId, operatorName, reason, ct);

    public Task<MsfxRemapInjectTaskResult> RemapInjectTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
        => _repo.RemapInjectTaskAsync(taskId, operatorName, reason, ct);

    public Task<MsfxMergeInjectTaskResult> MergeInjectTasksAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct)
        => _repo.MergeInjectTasksAsync(taskIds, operatorName, reason, ct);

    public Task<MsfxSplitInjectTaskResult> SplitInjectTaskAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct)
        => _repo.SplitInjectTaskAsync(taskId, splitMode, operatorName, reason, ct);

    public Task<IReadOnlyList<MsfxInjectTaskSplitUnitRow>> GetInjectTaskSplitUnitsAsync(long taskId, CancellationToken ct)
        => _repo.GetInjectTaskSplitUnitsAsync(taskId, ct);

    public Task<IReadOnlyList<MsfxInjectTaskSplitCodeRow>> GetInjectTaskSplitCodeRowsAsync(long taskId, CancellationToken ct)
        => _repo.GetInjectTaskSplitCodeRowsAsync(taskId, ct);

    public Task<MsfxSplitInjectTaskCustomResult> SplitInjectTaskCustomAsync(
        long taskId,
        IReadOnlyList<string> groupKeys,
        IReadOnlyList<int> bucketIndexes,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => _repo.SplitInjectTaskCustomAsync(taskId, groupKeys, bucketIndexes, operatorName, reason, ct);

    public Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        int limit,
        CancellationToken ct)
        => _repo.GetMappingBatchGroupsAsync(mapStatus, codeStatus, searchScope, keyword, limit, ct);

    public Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string? groupSourceDrugNameRaw,
        string? groupSourceSpecRaw,
        string? groupSourceNameNorm,
        string? groupSourceSpecNorm,
        string action,
        string? drugId,
        string? spec,
        CancellationToken ct)
        => _repo.PreviewMappingBatchByGroupAsync(
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            groupSourceDrugNameRaw,
            groupSourceSpecRaw,
            groupSourceNameNorm,
            groupSourceSpecNorm,
            action,
            drugId,
            spec,
            ct);

    public Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string? groupSourceDrugNameRaw,
        string? groupSourceSpecRaw,
        string? groupSourceNameNorm,
        string? groupSourceSpecNorm,
        string action,
        string? drugId,
        string? spec,
        CancellationToken ct)
        => _repo.ApplyMappingBatchByGroupAsync(
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            groupSourceDrugNameRaw,
            groupSourceSpecRaw,
            groupSourceNameNorm,
            groupSourceSpecNorm,
            action,
            drugId,
            spec,
            ct);
}
