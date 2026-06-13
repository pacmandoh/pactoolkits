using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IDashboardService
{
    DashboardQuery BuildQuery(DashboardFilter filter, int topN);

    Task<IReadOnlyList<string>> GetClientNamesAsync(CancellationToken ct);

    Task<DashboardSnapshot> LoadSnapshotAsync(DashboardLoadRequest request, CancellationToken ct);

    Task<PagedResult<TraceTxnDto>> LoadTxnPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TrendRowDto>> LoadTxnTrendPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TraceEntryLogDto>> LoadEntryPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<AbnormalRowDto>> LoadAbnormalPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct);
}
