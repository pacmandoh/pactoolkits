using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Services;

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

    public async Task<StockCellEditBatchResult> ApplyStockCellEditsAsync(
        IReadOnlyList<StockCellEditRequest> edits,
        CancellationToken ct)
    {
        if (edits.Count == 0)
        {
            return new StockCellEditBatchResult(0, 0, null);
        }

        var savedCount = 0;
        var failedCount = 0;
        string? lastError = null;

        foreach (var edit in edits)
        {
            try
            {
                await _repo.UpdateStockCellAsync(
                    edit.MatchTraceCode,
                    edit.ColumnHeader,
                    edit.NewValue,
                    ct).ConfigureAwait(false);
                savedCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                lastError = $"{edit.MatchTraceCode} {edit.ColumnHeader}: {ex.Message}";
            }
        }

        return new StockCellEditBatchResult(savedCount, failedCount, lastError);
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

    public async Task<StockReassignApplyResultDto> ReassignByTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        StockReassignContext context,
        CancellationToken ct)
    {
        var affected = 0;
        long auditId = 0;

        foreach (var traceCode in traceCodes)
        {
            if (string.IsNullOrWhiteSpace(traceCode))
            {
                continue;
            }

            var one = await _repo.ReassignStockByTraceCodeAsync(
                traceCode,
                context.TargetDrugId,
                context.TargetSpec,
                context.TargetQty,
                context.Reason,
                context.OperatorName,
                context.Source,
                ct).ConfigureAwait(false);

            affected += one.AffectedRows;
            if (auditId == 0)
            {
                auditId = one.AuditId;
            }
        }

        return new StockReassignApplyResultDto(affected, auditId);
    }

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
        => PinyinCatalogExpander.ExpandDrugKeywordAsync(_catalogCache, keyword, ct);
}
