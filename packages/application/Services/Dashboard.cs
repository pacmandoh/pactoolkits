using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

public sealed class DashboardService : IDashboardService
{
    private const int ChartPageSize = 200;
    private readonly IDashboardRepo _repo;

    public DashboardService(IDashboardRepo repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    private static DashboardQuery BuildQuery(DashboardFilter filter, int topN)
    {
        var client = InputNormalizer.Normalize(filter.ClientRaw);
        var drug = InputNormalizer.Normalize(filter.DrugId);
        var spec = InputNormalizer.Normalize(filter.Spec);

        return new DashboardQuery(
            Range: new DateRange(filter.From, filter.To),
            ClientName: client,
            DrugId: drug,
            Spec: spec,
            TopN: topN,
            TrendMetric: filter.TrendMetric);
    }

    private static DashboardQuery BuildChartQuery(DashboardFilter filter, int topN)
        => new(
            Range: new DateRange(filter.From, filter.To),
            ClientName: null,
            DrugId: null,
            Spec: null,
            TopN: topN,
            TrendMetric: filter.TrendMetric);

    public async Task<DashboardSnapshot> GetSnapshotAsync(DashboardRequest request, CancellationToken ct)
    {
        var qTop = BuildQuery(request.Filter, request.OverviewTopN);
        var qPaged = BuildQuery(request.Filter, topN: 0);
        var qChart = BuildChartQuery(request.Filter, ChartPageSize);

        var clientNamesTask = _repo.GetClientNamesAsync(ct);
        var kpiTask = _repo.GetKpisAsync(qTop, ct);
        var trendTask = _repo.GetTrendPageAsync(qTop, page: 1, pageSize: qTop.TopN, ct);
        var txnOverviewTask = _repo.GetRecentTxnsPageAsync(qTop, page: 1, pageSize: request.OverviewTopN, ct);
        var chartTrendTask = _repo.GetTrendPageAsync(qPaged, page: 1, pageSize: ChartPageSize, ct);
        var chartTxnsTask = _repo.GetRecentTxnsPageAsync(qPaged, page: 1, pageSize: ChartPageSize, ct);
        var txnPageTask = _repo.GetRecentTxnsPageAsync(qPaged, request.TxnPageIndex, request.TxnPageSize, ct);
        var txnTrendPageTask = _repo.GetTrendPageAsync(qPaged, request.TxnTrendPageIndex, request.TxnTrendPageSize, ct);
        var entryOverviewTask = _repo.GetEntryLogsPageAsync(qTop, page: 1, pageSize: request.EntryOverviewTopN, ct);
        var entryPageTask = _repo.GetEntryLogsPageAsync(qPaged, request.EntryPageIndex, request.EntryPageSize, ct);
        var topClientsTask = _repo.GetClientsAsync(qTop, ct);
        var chartClientsTask = request.RefreshDistributions
            ? _repo.GetClientsAsync(qChart, ct)
            : Task.FromResult<IReadOnlyList<(string Client, long Value)>>([]);
        var entryChartTask = request.RefreshDistributions
            ? _repo.GetEntryChartAsync(qChart, ct)
            : Task.FromResult<IReadOnlyList<EntryChartRowDto>>([]);
        var abnormalTask = _repo.GetAbnormalQueuePageAsync(qPaged, request.AbnormalPageIndex, request.AbnormalPageSize, ct);

        await Task.WhenAll(
            clientNamesTask,
            kpiTask,
            trendTask,
            txnOverviewTask,
            chartTrendTask,
            chartTxnsTask,
            txnPageTask,
            txnTrendPageTask,
            entryOverviewTask,
            entryPageTask,
            topClientsTask,
            chartClientsTask,
            entryChartTask,
            abnormalTask).ConfigureAwait(false);

        return new DashboardSnapshot(
            ClientNames: await clientNamesTask.ConfigureAwait(false),
            Kpi: await kpiTask.ConfigureAwait(false),
            Trend: (await trendTask.ConfigureAwait(false)).Rows,
            TxnsOverview: await txnOverviewTask.ConfigureAwait(false),
            TxnsPage: await txnPageTask.ConfigureAwait(false),
            TxnTrendPage: await txnTrendPageTask.ConfigureAwait(false),
            EntriesOverview: await entryOverviewTask.ConfigureAwait(false),
            EntriesPage: await entryPageTask.ConfigureAwait(false),
            TopClients: await topClientsTask.ConfigureAwait(false),
            ChartTrend: (await chartTrendTask.ConfigureAwait(false)).Rows,
            ChartTxns: (await chartTxnsTask.ConfigureAwait(false)).Rows,
            DistributionsRefreshed: request.RefreshDistributions,
            ChartClients: await chartClientsTask.ConfigureAwait(false),
            EntryChart: await entryChartTask.ConfigureAwait(false),
            Abnormal: await abnormalTask.ConfigureAwait(false));
    }

    public Task<PagedResult<TraceTxnDto>> GetTxnPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct)
        => _repo.GetRecentTxnsPageAsync(BuildQuery(filter, topN: 0), page, pageSize, ct);

    public Task<PagedResult<TrendRowDto>> GetTxnTrendPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct)
        => _repo.GetTrendPageAsync(BuildQuery(filter, topN: 0), page, pageSize, ct);

    public Task<PagedResult<TraceEntryLogDto>> GetEntryPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct)
        => _repo.GetEntryLogsPageAsync(BuildQuery(filter, topN: 0), page, pageSize, ct);

    public Task<PagedResult<AbnormalRowDto>> GetAbnormalPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct)
        => _repo.GetAbnormalQueuePageAsync(BuildQuery(filter, topN: 0), page, pageSize, ct);
}
