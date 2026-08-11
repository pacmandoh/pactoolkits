using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// Dashboard 查询数据访问；仅 SQL/分页，不含展示聚合口径
/// </summary>
public interface IDashboardRepo
{
    Task<IReadOnlyList<string>> GetClientNamesAsync(CancellationToken ct);

    Task<IReadOnlyList<(string Client, long Value)>> GetClientsAsync(DashboardQuery q, CancellationToken ct);

    Task<DashboardKpiDto> GetKpisAsync(DashboardQuery q, CancellationToken ct);

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

    Task<IReadOnlyList<EntryChartRowDto>> GetEntryChartAsync(DashboardQuery q, CancellationToken ct);

    Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct);

    Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct);
}
