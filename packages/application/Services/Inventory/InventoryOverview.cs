using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services;

/// <summary>
/// 实现库存关键字拼音扩展、单元格编辑和改派限制
/// </summary>
public sealed class InventoryOverviewService : IInventoryOverviewService
{
    public const int DefaultLargeBatchReassignConfirmThreshold = 500;

    private readonly IInventoryOverviewRepo _repo;
    private readonly IDrugIndexRepo _drugIndexRepo;
    private readonly IPinyinSearchCatalogCache _catalogCache;

    public InventoryOverviewService(
        IInventoryOverviewRepo repo,
        IDrugIndexRepo drugIndexRepo,
        IPinyinSearchCatalogCache catalogCache)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _drugIndexRepo = drugIndexRepo ?? throw new ArgumentNullException(nameof(drugIndexRepo));
        _catalogCache = catalogCache ?? throw new ArgumentNullException(nameof(catalogCache));
    }

    public int LargeBatchReassignConfirmThreshold => DefaultLargeBatchReassignConfirmThreshold;

    public async Task<PagedResult<TracePoolStockRowDto>> GetStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => await _repo.GetStockPageAsync(
            await BuildKeywordContextAsync(keyword, ct).ConfigureAwait(false),
            page,
            pageSize,
            ct).ConfigureAwait(false);

    public async Task<PagedResult<TracePoolDrugSpecAggDto>> GetDrugSpecAggPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => await _repo.GetDrugSpecAggPageAsync(
            await BuildKeywordContextAsync(keyword, ct).ConfigureAwait(false),
            page,
            pageSize,
            ct).ConfigureAwait(false);

    public async Task<PagedResult<LowStockRowDto>> GetLowStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => await _repo.GetLowStockPageAsync(
            await BuildKeywordContextAsync(keyword, ct).ConfigureAwait(false),
            page,
            pageSize,
            ct).ConfigureAwait(false);

    public async Task<PagedResult<MissingInventoryRowDto>> GetMissingInventoryPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => await _repo.GetMissingInventoryPageAsync(
            await BuildKeywordContextAsync(keyword, ct).ConfigureAwait(false),
            page,
            pageSize,
            ct).ConfigureAwait(false);

    public async Task<bool> TargetDrugSpecExistsAsync(string drugId, string spec, CancellationToken ct)
    {
        var dto = await _drugIndexRepo.GetByKeyAsync(drugId, spec, ct).ConfigureAwait(false);
        return dto is not null;
    }

    public async Task<StockRowEditBatchResult> ApplyStockRowEditsAsync(
        IReadOnlyList<StockRowEditRequest> edits,
        TraceCodeValidationRule traceCodeRule,
        CancellationToken ct)
    {
        if (edits.Count == 0)
        {
            return new StockRowEditBatchResult(
                0, 0, null, Array.Empty<StockRowEditSaved>(), Array.Empty<StockRowEditConflict>());
        }

        var executable = new List<StockRowEditRequest>(edits.Count);
        foreach (var edit in edits)
        {
            if (edit.NewTraceCode is null && edit.NewRemain is null)
            {
                continue;
            }

            if (edit.NewTraceCode is not null
                && !TraceCodeAnalyzer.TryValidateFormat(edit.NewTraceCode, traceCodeRule, out var formatError))
            {
                throw new ArgumentException($"{edit.MatchTraceCode}: {formatError}");
            }

            executable.Add(edit);
        }

        if (executable.Count == 0)
        {
            return new StockRowEditBatchResult(
                0, 0, null, Array.Empty<StockRowEditSaved>(), Array.Empty<StockRowEditConflict>());
        }

        return await _repo.ApplyStockRowEditsAsync(executable, ct).ConfigureAwait(false);
    }

    public Task<int> DeleteStockByTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct)
        => _repo.DeleteStockByTraceCodesAsync(traceCodes, ct);

    public async Task<StockReassignPreviewDto> PreviewReassignByKeywordAsync(
        string keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        int sampleLimit,
        CancellationToken ct)
        => await _repo.PreviewStockReassignByKeywordAsync(
            await BuildKeywordContextAsync(keyword, ct).ConfigureAwait(false),
            targetDrugId,
            targetSpec,
            targetQty,
            sampleLimit,
            ct).ConfigureAwait(false);

    public Task<StockReassignApplyResultDto> ReassignByTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        StockReassignContext context,
        CancellationToken ct)
        => _repo.ReassignStockByTraceCodesAsync(
            traceCodes,
            context.TargetDrugId,
            context.TargetSpec,
            context.TargetQty,
            context.Reason,
            context.OperatorName,
            context.Source,
            ct);

    public async Task<StockReassignApplyResultDto> ReassignByKeywordAsync(
        string keyword,
        StockReassignContext context,
        CancellationToken ct)
        => await _repo.ReassignStockByKeywordAsync(
            await BuildKeywordContextAsync(keyword, ct).ConfigureAwait(false),
            context.TargetDrugId,
            context.TargetSpec,
            context.TargetQty,
            context.Reason,
            context.OperatorName,
            context.Source,
            ct).ConfigureAwait(false);

    private Task<KeywordSearchContext> BuildKeywordContextAsync(string? keyword, CancellationToken ct)
        => PinyinExpansion.ExpandDrugKeywordAsync(_catalogCache, keyword, ct);
}
