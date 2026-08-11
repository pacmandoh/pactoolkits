using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services;

/// <summary>
/// 实现药品索引的拼音扩展检索、保存限制和主键修复编排
/// </summary>
public sealed class DrugIndexService : IDrugIndexService
{
    private readonly IDrugIndexRepo _repo;
    private readonly IPinyinSearchCatalogCache _catalogCache;

    public DrugIndexService(IDrugIndexRepo repo, IPinyinSearchCatalogCache catalogCache)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
    }

    public async Task<DrugIndexSearchResult> SearchAsync(string? keyword, int limit, CancellationToken ct)
    {
        var kw = (keyword ?? string.Empty).Trim();
        var cap = Math.Clamp(limit, 1, 2000);
        if (kw.Length == 0)
        {
            var rows = await _repo.SearchAsync(null, cap, ct).ConfigureAwait(false);
            var total = await _repo.CountAsync(null, ct).ConfigureAwait(false);
            return new DrugIndexSearchResult(rows, total);
        }

        var sqlMatches = await _repo.SearchAsync(kw, cap, ct).ConfigureAwait(false);
        var sqlTotal = await _repo.CountAsync(kw, ct).ConfigureAwait(false);
        if (!TextSearchHelper.LooksLikePinyinQuery(kw))
        {
            return new DrugIndexSearchResult(sqlMatches, sqlTotal);
        }

        var catalog = await _catalogCache.GetCatalogRowsAsync(ct).ConfigureAwait(false);
        var matchTotal = Math.Max(sqlTotal, CountCatalogMatches(kw, catalog));
        if (sqlMatches.Count >= cap)
        {
            return new DrugIndexSearchResult(sqlMatches, matchTotal);
        }

        var seen = new HashSet<(string DrugId, string Spec)>(
            sqlMatches.Select(static row => (row.DrugId, row.Spec)));

        var pinyinMatches = catalog
            .Where(row => !seen.Contains((row.DrugId, row.Spec))
                        && TextSearchHelper.MatchesAny(
                            kw,
                            row.DrugId,
                            row.Spec,
                            row.RuleKey,
                            row.PreTc,
                            row.Note))
            .Take(cap - sqlMatches.Count);

        var items = sqlMatches.Concat(pinyinMatches).Take(cap).ToArray();
        return new DrugIndexSearchResult(items, matchTotal);
    }

    private static int CountCatalogMatches(string keyword, IReadOnlyList<DrugIndexDto> catalog)
    {
        var count = 0;
        foreach (var row in catalog)
        {
            if (TextSearchHelper.MatchesAny(
                    keyword,
                    row.DrugId,
                    row.Spec,
                    row.RuleKey,
                    row.PreTc,
                    row.Note))
            {
                count++;
            }
        }

        return count;
    }

    public Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct)
        => _repo.GetByKeyAsync(drugId, spec, ct);

    public async Task<DrugIndexSaveResult> SaveAsync(DrugIndexSaveRequest request, CancellationToken ct)
    {
        if (!request.IsNew && request.HasPrimaryKeyChanges)
        {
            return new DrugIndexSaveResult(DrugSaveOutcome.BlockedPrimaryKeyChange, null, null);
        }

        if (!request.IsNew && request.HasQtyChanged)
        {
            var sourceDrugId = request.OriginDrugId ?? string.Empty;
            var sourceSpec = request.OriginSpec ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(sourceDrugId) && !string.IsNullOrWhiteSpace(sourceSpec))
            {
                var preview = await _repo.PreviewKeyFixAsync(
                    sourceDrugId,
                    sourceSpec,
                    sourceDrugId,
                    sourceSpec,
                    ct).ConfigureAwait(false);
                if (preview.TracePoolAffected > 0 || preview.TraceTxnAffected > 0)
                {
                    return new DrugIndexSaveResult(DrugSaveOutcome.BlockedQtyChange, null, null);
                }
            }
        }

        if (request.IsNew)
        {
            var exists = await _repo.ExistsAsync(request.Dto.DrugId, request.Dto.Spec, ct).ConfigureAwait(false);
            if (exists)
            {
                return new DrugIndexSaveResult(DrugSaveOutcome.BlockedDuplicate, null, null);
            }
        }

        try
        {
            var saved = await _repo.UpsertAsync(request.Dto, request.ExpectedVersion, ct).ConfigureAwait(false);
            _catalogCache.Invalidate();
            return new DrugIndexSaveResult(DrugSaveOutcome.Saved, saved, null);
        }
        catch (DrugIndexConcurrencyException cx)
        {
            return new DrugIndexSaveResult(DrugSaveOutcome.ConcurrencyConflict, cx.Current, cx);
        }
    }

    public async Task DeleteAsync(string drugId, string spec, long expectedVersion, CancellationToken ct)
    {
        await _repo.DeleteAsync(drugId, spec, expectedVersion, ct).ConfigureAwait(false);
        _catalogCache.Invalidate();
    }

    public Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        CancellationToken ct)
        => _repo.PreviewKeyFixAsync(sourceDrugId, sourceSpec, targetDrugId, targetSpec, ct);

    public async Task<DrugKeyFixCommitResult> ApplyKeyFixAsync(DrugKeyFixRequest request, CancellationToken ct)
    {
        var apply = await _repo.ApplyKeyFixAsync(
            request.Source,
            request.Target,
            request.Reason,
            request.OperatorName,
            request.SourceTag,
            ct).ConfigureAwait(false);

        _catalogCache.Invalidate();

        var sourceAfter = await _repo.GetByKeyAsync(request.Source.DrugId, request.Source.Spec, ct).ConfigureAwait(false);
        var targetAfter = await _repo.GetByKeyAsync(apply.Current.DrugId, apply.Current.Spec, ct).ConfigureAwait(false);
        return new DrugKeyFixCommitResult(apply, sourceAfter, targetAfter);
    }
}
