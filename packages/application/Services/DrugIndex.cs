using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services;

public interface IDrugIndexService
{
    Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct);

    Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct);

    Task<DrugIndexSaveResult> SaveAsync(DrugIndexSaveRequest request, CancellationToken ct);

    Task DeleteAsync(string drugId, string spec, CancellationToken ct);

    Task<DrugKeyFixPreviewDto> PreviewKeyFixAsync(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        CancellationToken ct);

    Task<DrugKeyFixCommitResult> ApplyKeyFixAsync(DrugKeyFixRequest request, CancellationToken ct);
}

public sealed class DrugIndexService : IDrugIndexService
{
    private readonly IDrugIndexRepo _repo;
    private readonly IPinyinSearchCatalogCache _catalogCache;

    public DrugIndexService(IDrugIndexRepo repo, IPinyinSearchCatalogCache catalogCache)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
    }

    public async Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct)
    {
        var kw = (keyword ?? string.Empty).Trim();
        var cap = Math.Clamp(limit, 1, 2000);
        if (kw.Length == 0)
        {
            return await _repo.SearchAsync(null, cap, ct).ConfigureAwait(false);
        }

        var sqlMatches = await _repo.SearchAsync(kw, cap, ct).ConfigureAwait(false);
        if (sqlMatches.Count >= cap || !TextSearchHelper.LooksLikePinyinQuery(kw))
        {
            return sqlMatches;
        }

        var catalog = await _catalogCache.GetCatalogRowsAsync(ct).ConfigureAwait(false);
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

        return sqlMatches.Concat(pinyinMatches).Take(cap).ToArray();
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
            var sourceDrugId = request.SelectedDrugId ?? request.OriginDrugId ?? string.Empty;
            var sourceSpec = request.SelectedSpec ?? request.OriginSpec ?? string.Empty;
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

    public async Task DeleteAsync(string drugId, string spec, CancellationToken ct)
    {
        await _repo.DeleteAsync(drugId, spec, ct).ConfigureAwait(false);
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
