using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

public sealed class InventoryOverviewService : IInventoryOverviewService
{
    public const int DefaultLargeBatchReassignConfirmThreshold = 500;

    private readonly IInventoryOverviewRepo _repo;
    private readonly IDrugIndexRepo _drugIndexRepo;

    public InventoryOverviewService(IInventoryOverviewRepo repo, IDrugIndexRepo drugIndexRepo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _drugIndexRepo = drugIndexRepo ?? throw new ArgumentNullException(nameof(drugIndexRepo));
    }

    public int LargeBatchReassignConfirmThreshold => DefaultLargeBatchReassignConfirmThreshold;

    public Task<PagedResult<TracePoolStockRowDto>> GetStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => _repo.GetStockPageAsync(keyword, page, pageSize, ct);

    public Task<PagedResult<TracePoolDrugSpecAggDto>> GetDrugSpecAggPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => _repo.GetDrugSpecAggPageAsync(keyword, page, pageSize, ct);

    public Task<PagedResult<LowStockRowDto>> GetLowStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => _repo.GetLowStockPageAsync(keyword, page, pageSize, ct);

    public Task<PagedResult<MissingInventoryRowDto>> GetMissingInventoryPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct)
        => _repo.GetMissingInventoryPageAsync(keyword, page, pageSize, ct);

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

    public Task<StockReassignPreviewDto> PreviewReassignByKeywordAsync(
        string keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        int sampleLimit,
        CancellationToken ct)
        => _repo.PreviewStockReassignByKeywordAsync(
            keyword,
            targetDrugId,
            targetSpec,
            targetQty,
            sampleLimit,
            ct);

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

    public Task<StockReassignApplyResultDto> ReassignByKeywordAsync(
        string keyword,
        StockReassignContext context,
        CancellationToken ct)
        => _repo.ReassignStockByKeywordAsync(
            keyword,
            context.TargetDrugId,
            context.TargetSpec,
            context.TargetQty,
            context.Reason,
            context.OperatorName,
            context.Source,
            ct);
}
