using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services;

public interface IMsfxSyncService
{
    Task<MsfxPullWindow> LoadPullWindowAsync(string sourceApi, CancellationToken ct);
    Task<MsfxPullBatchStartResult> StartMsfxPullBatchAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, CancellationToken ct);
    Task CompleteMsfxPullBatchAsync(long batchId, string status, int successCount, int failCount, string? errMsg, CancellationToken ct);
    Task UpdateMsfxPullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct);
    Task AdvanceMsfxPullCursorAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, long batchId, string batchStatus, CancellationToken ct);
    Task<IReadOnlyList<MsfxBillRetryRow>> LoadDueBillRetriesAsync(string sourceApi, int limit, CancellationToken ct);
    Task ScheduleBillRetryAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? lastError, CancellationToken ct);
    Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct);
    Task WatchMsfxBillAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? fromEntName, string? billType, string? billTime, string? billUploadTime, string? lastSeenStatus, string? rawJson, CancellationToken ct);
    Task<IReadOnlyList<MsfxBillWatchRow>> LoadDueBillWatchesAsync(string sourceApi, int limit, CancellationToken ct);
    Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct);
    Task RescheduleBillWatchAsync(string sourceApi, string billCode, string? lastSeenStatus, string? lastError, CancellationToken ct);
    Task<long> SaveInboundBillAsync(long batchId, string billCode, string billType, string billTime, string billUploadTime, string fromRefUserId, string fromEntName, string toRefUserId, string toUserId, string toUserName, string status, string rawJson, CancellationToken ct);
    Task<MsfxIngestDetailResult> IngestMsfxBillDetailAsync(long billId, string billCode, IReadOnlyList<(string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes)> drugs, CancellationToken ct);
    Task<MsfxMapApplyResult> ApplyMsfxMappingAsync(int limit, CancellationToken ct);
    Task<MsfxMappingStatusSnapshot> LoadMappingStatusSnapshotAsync(CancellationToken ct);
    Task<MsfxBuildTaskResult> BuildMsfxInjectTasksAsync(int maxGroups, CancellationToken ct);
    Task<MsfxAutoBoardSnapshot> LoadMsfxDashboardAsync(CancellationToken ct);
    Task<IReadOnlyList<MsfxPullBatchRow>> LoadRecentPullBatchesAsync(int limit, CancellationToken ct);
    Task<MsfxMappingQueuePage> LoadMappingQueuePageAsync(int pageSize, string? mapStatus, string? codeStatus, string? searchScope, string? keyword, DateTimeOffset? cursorUpdatedAt, long? cursorId, bool newer, bool seekLastPage, CancellationToken ct);
    Task<IReadOnlyList<MsfxInjectTaskQueueRow>> LoadInjectTaskQueueAsync(int limit, CancellationToken ct);
    Task<MsfxReopenInjectTaskResult> ReopenMsfxTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);
    Task<MsfxDiscardInjectTaskResult> DiscardMsfxTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);
    Task<MsfxRemapInjectTaskResult> RemapMsfxTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);
    Task<MsfxMergeInjectTaskResult> MergeMsfxTasksAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct);
    Task<MsfxSplitInjectTaskResult> SplitMsfxTaskAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct);
    Task<IReadOnlyList<MsfxInjectTaskSplitUnitRow>> LoadMsfxTaskSplitUnitsAsync(long taskId, CancellationToken ct);
    Task<IReadOnlyList<MsfxInjectTaskSplitCodeRow>> LoadMsfxTaskSplitCodeRowsAsync(long taskId, CancellationToken ct);
    Task<MsfxSplitInjectTaskCustomResult> SplitMsfxTaskCustomAsync(long taskId, IReadOnlyList<string> groupKeys, IReadOnlyList<int> bucketIndexes, string? operatorName, string? reason, CancellationToken ct);
    Task<IReadOnlyList<MsfxMappingBatchGroupRow>> LoadMappingBatchGroupsAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, int limit, CancellationToken ct);
    Task<MsfxMappingBatchPreview> PreviewMsfxMappingBatchAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, string? groupSourceDrugNameRaw, string? groupSourceSpecRaw, string? groupSourceNameNorm, string? groupSourceSpecNorm, string action, string? drugId, string? spec, CancellationToken ct);
    Task<MsfxMappingBatchApplyResult> ApplyMsfxMappingBatchAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, string? groupSourceDrugNameRaw, string? groupSourceSpecRaw, string? groupSourceNameNorm, string? groupSourceSpecNorm, string action, string? drugId, string? spec, CancellationToken ct);
}

public sealed class MsfxSyncService : IMsfxSyncService
{
    private readonly IMsfxSyncRepo _repo;
    private readonly IPinyinSearchCatalogCache _catalogCache;
    private readonly PinyinKeywordExpansionCache _expansionCache = new();

    public MsfxSyncService(IMsfxSyncRepo repo, IPinyinSearchCatalogCache catalogCache)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
    }

    public Task<MsfxPullWindow> LoadPullWindowAsync(string sourceApi, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        return _repo.GetPullWindowAsync(sourceApi, ct);
    }

    public Task<MsfxPullBatchStartResult> StartMsfxPullBatchAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        if (endAt <= beginAt)
        {
            throw new ArgumentException("MSFX pull batch end time must be later than begin time.", nameof(endAt));
        }

        return _repo.StartPullBatchAsync(sourceApi, beginAt, endAt, ct);
    }

    public Task CompleteMsfxPullBatchAsync(long batchId, string status, int successCount, int failCount, string? errMsg, CancellationToken ct)
    {
        EnsurePositiveId(batchId, nameof(batchId));
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("MSFX pull batch status is required.", nameof(status));
        }

        if (successCount < 0 || failCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(successCount), "MSFX pull batch counts cannot be negative.");
        }

        return _repo.FinishPullBatchAsync(batchId, status.Trim(), successCount, failCount, errMsg, ct);
    }

    public Task UpdateMsfxPullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct)
    {
        EnsurePositiveId(batchId, nameof(batchId));
        return _repo.UpdatePullBatchRequestIdAsync(batchId, requestId, ct);
    }

    public Task AdvanceMsfxPullCursorAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, long batchId, string batchStatus, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        EnsurePositiveId(batchId, nameof(batchId));
        if (endAt <= beginAt)
        {
            throw new ArgumentException("MSFX pull cursor end time must be later than begin time.", nameof(endAt));
        }

        if (string.IsNullOrWhiteSpace(batchStatus))
        {
            throw new ArgumentException("MSFX pull cursor batch status is required.", nameof(batchStatus));
        }

        return _repo.AdvancePullCursorAsync(sourceApi, beginAt, endAt, batchId, batchStatus.Trim(), ct);
    }

    public Task<IReadOnlyList<MsfxBillRetryRow>> LoadDueBillRetriesAsync(string sourceApi, int limit, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        return _repo.GetDueBillRetriesAsync(sourceApi, NormalizeLimit(limit), ct);
    }

    public Task ScheduleBillRetryAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? lastError, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        EnsureText(billCode, nameof(billCode));
        return _repo.UpsertBillRetryAsync(sourceApi, billCode.Trim(), fromRefUserId, toRefUserId, lastError, ct);
    }

    public Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        EnsureText(billCode, nameof(billCode));
        return _repo.MarkBillRetrySucceededAsync(sourceApi, billCode.Trim(), ct);
    }

    public Task WatchMsfxBillAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? fromEntName, string? billType, string? billTime, string? billUploadTime, string? lastSeenStatus, string? rawJson, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        EnsureText(billCode, nameof(billCode));
        return _repo.UpsertBillWatchAsync(sourceApi, billCode.Trim(), fromRefUserId, toRefUserId, fromEntName, billType, billTime, billUploadTime, lastSeenStatus, rawJson, ct);
    }

    public Task<IReadOnlyList<MsfxBillWatchRow>> LoadDueBillWatchesAsync(string sourceApi, int limit, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        return _repo.GetDueBillWatchesAsync(sourceApi, NormalizeLimit(limit), ct);
    }

    public Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        EnsureText(billCode, nameof(billCode));
        return _repo.MarkBillWatchResolvedAsync(sourceApi, billCode.Trim(), ct);
    }

    public Task RescheduleBillWatchAsync(string sourceApi, string billCode, string? lastSeenStatus, string? lastError, CancellationToken ct)
    {
        EnsureSourceApi(sourceApi);
        EnsureText(billCode, nameof(billCode));
        return _repo.RescheduleBillWatchAsync(sourceApi, billCode.Trim(), lastSeenStatus, lastError, ct);
    }

    public Task<long> SaveInboundBillAsync(long batchId, string billCode, string billType, string billTime, string billUploadTime, string fromRefUserId, string fromEntName, string toRefUserId, string toUserId, string toUserName, string status, string rawJson, CancellationToken ct)
    {
        EnsurePositiveId(batchId, nameof(batchId));
        EnsureText(billCode, nameof(billCode));
        EnsureText(status, nameof(status));
        return _repo.UpsertInboundBillAsync(batchId, billCode.Trim(), billType, billTime, billUploadTime, fromRefUserId, fromEntName, toRefUserId, toUserId, toUserName, status.Trim(), rawJson, ct);
    }

    public Task<MsfxIngestDetailResult> IngestMsfxBillDetailAsync(long billId, string billCode, IReadOnlyList<(string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes)> drugs, CancellationToken ct)
    {
        EnsurePositiveId(billId, nameof(billId));
        EnsureText(billCode, nameof(billCode));
        ArgumentNullException.ThrowIfNull(drugs);
        return _repo.IngestUpoutDetailAsync(billId, billCode.Trim(), drugs, ct);
    }

    public Task<MsfxMapApplyResult> ApplyMsfxMappingAsync(int limit, CancellationToken ct)
        => _repo.ApplyMappingAsync(NormalizeLimit(limit), ct);

    public Task<MsfxMappingStatusSnapshot> LoadMappingStatusSnapshotAsync(CancellationToken ct)
        => _repo.GetMappingStatusSnapshotAsync(ct);

    public Task<MsfxBuildTaskResult> BuildMsfxInjectTasksAsync(int maxGroups, CancellationToken ct)
        => _repo.BuildInjectTasksAsync(NormalizeLimit(maxGroups), ct);

    public Task<MsfxAutoBoardSnapshot> LoadMsfxDashboardAsync(CancellationToken ct)
        => _repo.GetAutoBoardSnapshotAsync(ct);

    public Task<IReadOnlyList<MsfxPullBatchRow>> LoadRecentPullBatchesAsync(int limit, CancellationToken ct)
        => _repo.GetRecentPullBatchesAsync(NormalizeLimit(limit), ct);

    public async Task<MsfxMappingQueuePage> LoadMappingQueuePageAsync(int pageSize, string? mapStatus, string? codeStatus, string? searchScope, string? keyword, DateTimeOffset? cursorUpdatedAt, long? cursorId, bool newer, bool seekLastPage, CancellationToken ct)
        => await _repo.GetMappingQueuePageAsync(
            NormalizeLimit(pageSize),
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            await BuildPinyinExactPerTokenAsync(keyword, ct).ConfigureAwait(false),
            cursorUpdatedAt,
            cursorId,
            newer,
            seekLastPage,
            ct).ConfigureAwait(false);

    public Task<IReadOnlyList<MsfxInjectTaskQueueRow>> LoadInjectTaskQueueAsync(int limit, CancellationToken ct)
        => _repo.GetInjectTaskQueueAsync(limit < 0 ? 0 : limit, ct);

    public Task<MsfxReopenInjectTaskResult> ReopenMsfxTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        EnsurePositiveId(taskId, nameof(taskId));
        return _repo.ReopenInjectTaskAsync(taskId, operatorName, reason, ct);
    }

    public Task<MsfxDiscardInjectTaskResult> DiscardMsfxTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        EnsurePositiveId(taskId, nameof(taskId));
        return _repo.DiscardInjectTaskAsync(taskId, operatorName, reason, ct);
    }

    public Task<MsfxRemapInjectTaskResult> RemapMsfxTaskAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        EnsurePositiveId(taskId, nameof(taskId));
        return _repo.RemapInjectTaskAsync(taskId, operatorName, reason, ct);
    }

    public Task<MsfxMergeInjectTaskResult> MergeMsfxTasksAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taskIds);
        if (taskIds.Count < 2)
        {
            throw new ArgumentException("At least two MSFX tasks are required for merge.", nameof(taskIds));
        }

        if (taskIds.Any(x => x <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(taskIds), "MSFX task ids must be positive.");
        }

        return _repo.MergeInjectTasksAsync(taskIds, operatorName, reason, ct);
    }

    public Task<MsfxSplitInjectTaskResult> SplitMsfxTaskAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct)
    {
        EnsurePositiveId(taskId, nameof(taskId));
        EnsureText(splitMode, nameof(splitMode));
        return _repo.SplitInjectTaskAsync(taskId, splitMode.Trim(), operatorName, reason, ct);
    }

    public Task<IReadOnlyList<MsfxInjectTaskSplitUnitRow>> LoadMsfxTaskSplitUnitsAsync(long taskId, CancellationToken ct)
    {
        EnsurePositiveId(taskId, nameof(taskId));
        return _repo.GetInjectTaskSplitUnitsAsync(taskId, ct);
    }

    public Task<IReadOnlyList<MsfxInjectTaskSplitCodeRow>> LoadMsfxTaskSplitCodeRowsAsync(long taskId, CancellationToken ct)
    {
        EnsurePositiveId(taskId, nameof(taskId));
        return _repo.GetInjectTaskSplitCodeRowsAsync(taskId, ct);
    }

    public Task<MsfxSplitInjectTaskCustomResult> SplitMsfxTaskCustomAsync(long taskId, IReadOnlyList<string> groupKeys, IReadOnlyList<int> bucketIndexes, string? operatorName, string? reason, CancellationToken ct)
    {
        EnsurePositiveId(taskId, nameof(taskId));
        ArgumentNullException.ThrowIfNull(groupKeys);
        ArgumentNullException.ThrowIfNull(bucketIndexes);
        if (groupKeys.Count == 0 || bucketIndexes.Count == 0 || groupKeys.Count != bucketIndexes.Count)
        {
            throw new ArgumentException("Custom split requires matching group keys and bucket indexes.");
        }

        return _repo.SplitInjectTaskCustomAsync(taskId, groupKeys, bucketIndexes, operatorName, reason, ct);
    }

    public async Task<IReadOnlyList<MsfxMappingBatchGroupRow>> LoadMappingBatchGroupsAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, int limit, CancellationToken ct)
        => await _repo.GetMappingBatchGroupsAsync(
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            await BuildPinyinExactPerTokenAsync(keyword, ct).ConfigureAwait(false),
            NormalizeLimit(limit),
            ct).ConfigureAwait(false);

    public async Task<MsfxMappingBatchPreview> PreviewMsfxMappingBatchAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, string? groupSourceDrugNameRaw, string? groupSourceSpecRaw, string? groupSourceNameNorm, string? groupSourceSpecNorm, string action, string? drugId, string? spec, CancellationToken ct)
    {
        EnsureText(action, nameof(action));
        return await _repo.PreviewMappingBatchByGroupAsync(
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            await BuildPinyinExactPerTokenAsync(keyword, ct).ConfigureAwait(false),
            groupSourceDrugNameRaw,
            groupSourceSpecRaw,
            groupSourceNameNorm,
            groupSourceSpecNorm,
            action.Trim(),
            drugId,
            spec,
            ct).ConfigureAwait(false);
    }

    public async Task<MsfxMappingBatchApplyResult> ApplyMsfxMappingBatchAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, string? groupSourceDrugNameRaw, string? groupSourceSpecRaw, string? groupSourceNameNorm, string? groupSourceSpecNorm, string action, string? drugId, string? spec, CancellationToken ct)
    {
        EnsureText(action, nameof(action));
        return await _repo.ApplyMappingBatchByGroupAsync(
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            await BuildPinyinExactPerTokenAsync(keyword, ct).ConfigureAwait(false),
            groupSourceDrugNameRaw,
            groupSourceSpecRaw,
            groupSourceNameNorm,
            groupSourceSpecNorm,
            action.Trim(),
            drugId,
            spec,
            ct).ConfigureAwait(false);
    }

    private async Task<string[][]?> BuildPinyinExactPerTokenAsync(string? keyword, CancellationToken ct)
    {
        var normalized = (keyword ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (_expansionCache.TryGet(normalized, out var cached))
        {
            return cached;
        }

        var texts = await _catalogCache.GetSearchTextsAsync(ct).ConfigureAwait(false);
        var exactPerToken = TextSearchHelper.BuildPinyinExactPerToken(normalized, texts);
        _expansionCache.Set(normalized, exactPerToken);
        return exactPerToken;
    }

    private static void EnsureSourceApi(string sourceApi)
        => EnsureText(sourceApi, nameof(sourceApi));

    private static void EnsurePositiveId(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Identifier must be positive.");
        }
    }

    private static void EnsureText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }

    private static int NormalizeLimit(int limit)
        => limit <= 0 ? 1 : Math.Min(limit, 50000);
}
