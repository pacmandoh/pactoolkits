using PacToolkits.Application.DTOs;
using PacToolkits.Application.TextSearch;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 库存总览分页与单元格编辑的数据访问
/// </summary>
public interface IInventoryOverviewRepo
{
    Task<PagedResult<TracePoolStockRowDto>> GetStockPageAsync(
        KeywordSearchContext keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TracePoolDrugSpecAggDto>> GetDrugSpecAggPageAsync(
        KeywordSearchContext keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<LowStockRowDto>> GetLowStockPageAsync(
        KeywordSearchContext keyword,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<MissingInventoryRowDto>> GetMissingInventoryPageAsync(
        KeywordSearchContext keyword,
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
        KeywordSearchContext keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        int sampleLimit,
        CancellationToken ct);

    Task<StockReassignApplyResultDto> ReassignStockByKeywordAsync(
        KeywordSearchContext keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        string reason,
        string operatorName,
        string source,
        CancellationToken ct);
}
