using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IInventoryOverviewRepo
{
    Task<PagedResult<TracePoolStockRowDto>> GetStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TracePoolDrugSpecAggDto>> GetDrugSpecAggPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<LowStockRowDto>> GetLowStockPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<MissingInventoryRowDto>> GetMissingInventoryPageAsync(
        string? keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task UpdateStockCellAsync(
        string traceCode,
        string columnHeader,
        string? rawValue,
        CancellationToken ct);

    Task<int> DeleteStockByTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        CancellationToken ct);

    Task<StockReassignPreviewDto> PreviewStockReassignByTraceCodeAsync(
        string traceCode,
        string targetDrugId,
        string targetSpec,
        CancellationToken ct);

    Task<StockReassignApplyResultDto> ReassignStockByTraceCodeAsync(
        string traceCode,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        string reason,
        string operatorName,
        string source,
        CancellationToken ct);

    Task<StockReassignPreviewDto> PreviewStockReassignByKeywordAsync(
        string keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        int sampleLimit,
        CancellationToken ct);

    Task<StockReassignApplyResultDto> ReassignStockByKeywordAsync(
        string keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        string reason,
        string operatorName,
        string source,
        CancellationToken ct);
}
