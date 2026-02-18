using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using pactoolkits_ui.Contracts;

namespace pactoolkits_ui.Repositories;

public interface IDashboardRepo
{
    Task<IReadOnlyList<string>> GetClientNamesAsync(CancellationToken ct);

    Task<IReadOnlyList<(string Client, long Value)>> GetClientsAsync(DashboardQuery q, CancellationToken ct);

    Task<DashboardKpiDto> GetKpisAsync(
        DashboardQuery q,
        CancellationToken ct);

    Task<PagedResult<TrendRowDto>> GetTrendPageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TraceTxnDto>> GetRecentTxnsPageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<AbnormalRowDto>> GetAbnormalQueuePageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TraceEntryLogDto>> GetEntryLogsPageAsync(
        DashboardQuery q,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct);
}

public interface IDrugIndexRepo
{
    Task<IReadOnlyList<DrugIndexDto>> SearchAsync(string? keyword, int limit, CancellationToken ct);

    Task<DrugIndexDto?> GetByKeyAsync(string drugId, string spec, CancellationToken ct);

    Task<bool> ExistsAsync(string drugId, string spec, CancellationToken ct);

    Task<DrugIndexDto> UpsertAsync(DrugIndexDto dto, long? expectedVersion, CancellationToken ct);

    Task DeleteAsync(string drugId, string spec, CancellationToken ct);
}

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

public sealed record ScanCodeInsertResult(int RequestedCount, int InsertedCount, int SkippedCount);

public interface IScanCodeRepo
{
    Task<ScanCodeInsertResult> InsertTraceCodesAsync(
        string drugId,
        string spec,
        int qty,
        IReadOnlyList<string> traceCodes,
        CancellationToken ct);
}
