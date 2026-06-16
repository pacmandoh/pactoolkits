using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

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

    public DrugIndexService(IDrugIndexRepo repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct)
        => _repo.SearchAsync(keyword, limit, ct);

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
            return new DrugIndexSaveResult(DrugSaveOutcome.Saved, saved, null);
        }
        catch (DrugIndexConcurrencyException cx)
        {
            return new DrugIndexSaveResult(DrugSaveOutcome.ConcurrencyConflict, cx.Current, cx);
        }
    }

    public Task DeleteAsync(string drugId, string spec, CancellationToken ct)
        => _repo.DeleteAsync(drugId, spec, ct);

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

        var sourceAfter = await _repo.GetByKeyAsync(request.Source.DrugId, request.Source.Spec, ct).ConfigureAwait(false);
        var targetAfter = await _repo.GetByKeyAsync(apply.Current.DrugId, apply.Current.Spec, ct).ConfigureAwait(false);
        return new DrugKeyFixCommitResult(apply, sourceAfter, targetAfter);
    }
}
