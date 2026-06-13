using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

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

    Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct);

    Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct);
}
