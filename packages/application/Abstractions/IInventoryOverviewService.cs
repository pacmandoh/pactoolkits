using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 提供库存关键字扩展、行级批量编辑和大批量改派限制
/// </summary>
public interface IInventoryOverviewService
{
    int LargeBatchReassignConfirmThreshold { get; }

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

    Task<bool> TargetDrugSpecExistsAsync(string drugId, string spec, CancellationToken ct);

    Task<StockRowEditBatchResult> ApplyStockRowEditsAsync(
        IReadOnlyList<StockRowEditRequest> edits,
        TraceCodeValidationRule traceCodeRule,
        CancellationToken ct);

    Task<int> DeleteStockByTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct);

    Task<StockReassignPreviewDto> PreviewReassignByKeywordAsync(
        string keyword,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        int sampleLimit,
        CancellationToken ct);

    Task<StockReassignApplyResultDto> ReassignByTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        StockReassignContext context,
        CancellationToken ct);

    Task<StockReassignApplyResultDto> ReassignByKeywordAsync(
        string keyword,
        StockReassignContext context,
        CancellationToken ct);
}
