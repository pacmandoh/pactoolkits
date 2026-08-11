using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 定义 Dashboard 快照与分页查询的业务用例契约
/// </summary>
public interface IDashboardService
{
    Task<DashboardSnapshot> GetSnapshotAsync(DashboardRequest request, CancellationToken ct);

    Task<PagedResult<TraceTxnDto>> GetTxnPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TrendRowDto>> GetTxnTrendPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<TraceEntryLogDto>> GetEntryPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct);

    Task<PagedResult<AbnormalRowDto>> GetAbnormalPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct);
}
