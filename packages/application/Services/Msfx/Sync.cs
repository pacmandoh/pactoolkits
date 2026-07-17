using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services.Msfx;

public interface ISyncService
{
    Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct);
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
    Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(int pageSize, string? mapStatus, string? codeStatus, string? searchScope, string? keyword, DateTimeOffset? cursorUpdatedAt, long? cursorId, bool newer, bool seekLastPage, CancellationToken ct);
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

public sealed class SyncService : ISyncService, IMsfxAutoRunStore
{
    private readonly IMsfxSyncRepo _repo;
    private readonly IPinyinSearchCatalogCache _catalogCache;
    private readonly PinyinExpansionCache _expansionCache = new();

    public SyncService(IMsfxSyncRepo repo, IPinyinSearchCatalogCache catalogCache)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
    }

    public Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        return _repo.GetPullWindowAsync(sourceApi, ct);
    }

    public Task<MsfxPullBatchStartResult> StartPullBatchAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        if (endAt <= beginAt)
        {
            throw new ArgumentException("MSFX pull batch end time must be later than begin time.", nameof(endAt));
        }

        return _repo.StartPullBatchAsync(sourceApi, beginAt, endAt, ct);
    }

    public Task FinishPullBatchAsync(long batchId, string status, int successCount, int failCount, string? errMsg, CancellationToken ct)
    {
        RequirePositiveId(batchId, nameof(batchId));
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

    public Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct)
    {
        RequirePositiveId(batchId, nameof(batchId));
        return _repo.UpdatePullBatchRequestIdAsync(batchId, requestId, ct);
    }

    public Task AdvancePullCursorAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, long batchId, string batchStatus, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequirePositiveId(batchId, nameof(batchId));
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

    public Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(string sourceApi, int limit, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        return _repo.GetDueBillRetriesAsync(sourceApi, NormalizeLimit(limit), ct);
    }

    public Task UpsertBillRetryAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? lastError, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.UpsertBillRetryAsync(sourceApi, billCode.Trim(), fromRefUserId, toRefUserId, lastError, ct);
    }

    public Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.MarkBillRetrySucceededAsync(sourceApi, billCode.Trim(), ct);
    }

    public Task UpsertBillWatchAsync(string sourceApi, string billCode, string? fromRefUserId, string? toRefUserId, string? fromEntName, string? billType, string? billTime, string? billUploadTime, string? lastSeenStatus, string? rawJson, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.UpsertBillWatchAsync(sourceApi, billCode.Trim(), fromRefUserId, toRefUserId, fromEntName, billType, billTime, billUploadTime, lastSeenStatus, rawJson, ct);
    }

    public Task<IReadOnlyList<MsfxBillWatchRow>> GetDueBillWatchesAsync(string sourceApi, int limit, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        return _repo.GetDueBillWatchesAsync(sourceApi, NormalizeLimit(limit), ct);
    }

    public Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.MarkBillWatchResolvedAsync(sourceApi, billCode.Trim(), ct);
    }

    public Task RescheduleBillWatchAsync(string sourceApi, string billCode, string? lastSeenStatus, string? lastError, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequireText(billCode, nameof(billCode));
        return _repo.RescheduleBillWatchAsync(sourceApi, billCode.Trim(), lastSeenStatus, lastError, ct);
    }

    public Task<long> UpsertInboundBillAsync(long batchId, string billCode, string billType, string billTime, string billUploadTime, string fromRefUserId, string fromEntName, string toRefUserId, string toUserId, string toUserName, string status, string rawJson, CancellationToken ct)
    {
        RequirePositiveId(batchId, nameof(batchId));
        RequireText(billCode, nameof(billCode));
        RequireText(status, nameof(status));
        return _repo.UpsertInboundBillAsync(batchId, billCode.Trim(), billType, billTime, billUploadTime, fromRefUserId, fromEntName, toRefUserId, toUserId, toUserName, status.Trim(), rawJson, ct);
    }

    public Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(long billId, string billCode, IReadOnlyList<(string DrugName, string PackageSpec, string PrepnSpec, string BatchNo, IReadOnlyList<(string Code, string CodeLevel, string? Level1Code, string? Level2Code, string? Level3Code, string? Level4Code, string? Level5Code)> Codes)> drugs, CancellationToken ct)
    {
        RequirePositiveId(billId, nameof(billId));
        RequireText(billCode, nameof(billCode));
        ArgumentNullException.ThrowIfNull(drugs);
        return _repo.IngestUpoutDetailAsync(billId, billCode.Trim(), drugs, ct);
    }

    public Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct)
        => _repo.ApplyMappingAsync(NormalizeLimit(limit), ct);

    public Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct)
        => _repo.GetMappingStatusSnapshotAsync(ct);

    public Task<MsfxBuildInject> BuildInjectsAsync(int maxGroups, CancellationToken ct)
        => _repo.BuildInjectsAsync(NormalizeLimit(maxGroups), ct);

    public Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct)
        => _repo.GetAutoBoardSnapshotAsync(ct);

    public Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct)
        => _repo.GetRecentPullBatchesAsync(NormalizeLimit(limit), ct);

    public async Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(int pageSize, string? mapStatus, string? codeStatus, string? searchScope, string? keyword, DateTimeOffset? cursorUpdatedAt, long? cursorId, bool newer, bool seekLastPage, CancellationToken ct)
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

    public Task<IReadOnlyList<MsfxInjectQueueRow>> GetInjectQueueAsync(int limit, CancellationToken ct)
        => _repo.GetInjectQueueAsync(limit < 0 ? 0 : limit, ct);

    public Task<MsfxInjectReopen> ReopenInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.ReopenInjectAsync(taskId, operatorName, reason, ct);
    }

    public Task<MsfxInjectDiscard> DiscardInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.DiscardInjectAsync(taskId, operatorName, reason, ct);
    }

    public Task<MsfxInjectRemap> RemapInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.RemapInjectAsync(taskId, operatorName, reason, ct);
    }

    public Task<MsfxInjectMerge> MergeInjectsAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct)
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

        return _repo.MergeInjectsAsync(taskIds, operatorName, reason, ct);
    }

    public Task<MsfxInjectSplit> SplitInjectAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        RequireText(splitMode, nameof(splitMode));
        return _repo.SplitInjectAsync(taskId, splitMode.Trim(), operatorName, reason, ct);
    }

    public Task<IReadOnlyList<MsfxInjectSplitUnitRow>> GetInjectSplitUnitsAsync(long taskId, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.GetInjectSplitUnitsAsync(taskId, ct);
    }

    public Task<IReadOnlyList<MsfxInjectSplitCodeRow>> GetInjectSplitCodeRowsAsync(long taskId, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.GetInjectSplitCodeRowsAsync(taskId, ct);
    }

    public Task<MsfxInjectSplitCustom> SplitInjectCustomAsync(long taskId, IReadOnlyList<string> groupKeys, IReadOnlyList<int> bucketIndexes, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        ArgumentNullException.ThrowIfNull(groupKeys);
        ArgumentNullException.ThrowIfNull(bucketIndexes);
        if (groupKeys.Count == 0 || bucketIndexes.Count == 0 || groupKeys.Count != bucketIndexes.Count)
        {
            throw new ArgumentException("Custom split requires matching group keys and bucket indexes.");
        }

        return _repo.SplitInjectCustomAsync(taskId, groupKeys, bucketIndexes, operatorName, reason, ct);
    }

    public async Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, int limit, CancellationToken ct)
        => await _repo.GetMappingBatchGroupsAsync(
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            await BuildPinyinExactPerTokenAsync(keyword, ct).ConfigureAwait(false),
            NormalizeLimit(limit),
            ct).ConfigureAwait(false);

    public async Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, string? groupSourceDrugNameRaw, string? groupSourceSpecRaw, string? groupSourceNameNorm, string? groupSourceSpecNorm, string action, string? drugId, string? spec, CancellationToken ct)
    {
        RequireText(action, nameof(action));
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

    public async Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(string? mapStatus, string? codeStatus, string? searchScope, string? keyword, string? groupSourceDrugNameRaw, string? groupSourceSpecRaw, string? groupSourceNameNorm, string? groupSourceSpecNorm, string action, string? drugId, string? spec, CancellationToken ct)
    {
        RequireText(action, nameof(action));
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

    private static void RequireSourceApi(string sourceApi)
        => RequireText(sourceApi, nameof(sourceApi));

    private static void RequirePositiveId(long value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Identifier must be positive.");
        }
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }

    private static int NormalizeLimit(int limit)
        => limit <= 0 ? 1 : Math.Min(limit, 50000);
}
