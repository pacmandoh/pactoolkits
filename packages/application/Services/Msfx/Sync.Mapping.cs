using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services.Msfx;

public sealed partial class SyncService
{
    public async Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
        int pageSize,
        IReadOnlyCollection<string>? mapStatuses,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        DateTimeOffset? cursorUpdatedAt,
        long? cursorId,
        bool newer,
        bool seekLastPage,
        CancellationToken ct)
        => await _mapping.GetMappingQueuePageAsync(
            NormalizeLimit(pageSize),
            mapStatuses,
            codeStatus,
            searchScope,
            keyword,
            await BuildPinyinExactPerTokenAsync(keyword, ct).ConfigureAwait(false),
            cursorUpdatedAt,
            cursorId,
            newer,
            seekLastPage,
            ct).ConfigureAwait(false);

    public Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct)
        => _mapping.GetMappingStatusSnapshotAsync(ct);

    public Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct)
        => _mapping.ApplyMappingAsync(NormalizeLimit(limit), ct);

    public async Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        int limit,
        CancellationToken ct)
        => await _mapping.GetMappingBatchGroupsAsync(
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            await BuildPinyinExactPerTokenAsync(keyword, ct).ConfigureAwait(false),
            NormalizeLimit(limit),
            ct).ConfigureAwait(false);

    public async Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(
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
    {
        RequireText(action, nameof(action));
        return await _mapping.PreviewMappingBatchByGroupAsync(
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

    public async Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(
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
    {
        RequireText(action, nameof(action));
        return await _mapping.ApplyMappingBatchByGroupAsync(
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
}
