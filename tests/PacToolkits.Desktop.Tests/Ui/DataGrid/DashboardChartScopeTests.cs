using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class DashboardChartScopeTests
{
    [Fact]
    public async Task Distribution_queries_ignore_client_and_keep_date_drug_spec()
    {
        var repo = new Repo();
        var service = new DashboardService(repo);

        await service.GetSnapshotAsync(CreateRequest(refreshDistributions: true), CancellationToken.None);

        Assert.Equal(2, repo.ClientQueries.Count);
        var chartQuery = Assert.Single(repo.ClientQueries, query => query.TopN == 200);
        Assert.Empty(chartQuery.ClientMachines);
        Assert.Equal("drug-a", chartQuery.DrugId);
        Assert.Equal("10mg", chartQuery.Spec);
        Assert.Equal(new DateRange(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 16)), chartQuery.Range);
        Assert.NotNull(repo.EntryChartQuery);
        Assert.Empty(repo.EntryChartQuery.ClientMachines);
        Assert.Equal("drug-a", repo.EntryChartQuery.DrugId);
        Assert.Equal("10mg", repo.EntryChartQuery.Spec);
        Assert.Equal(chartQuery.Range, repo.EntryChartQuery.Range);
    }

    [Fact]
    public async Task Drug_and_transaction_chart_queries_keep_all_filters()
    {
        var repo = new Repo();
        var service = new DashboardService(repo);

        await service.GetSnapshotAsync(CreateRequest(refreshDistributions: true), CancellationToken.None);

        var trendChartQuery = Assert.Single(repo.TrendQueries, item => item.PageSize == 200).Query;
        Assert.Equal(["client-a"], trendChartQuery.ClientMachines);
        Assert.Equal("drug-a", trendChartQuery.DrugId);
        Assert.Equal("10mg", trendChartQuery.Spec);

        var txnChartQuery = Assert.Single(repo.TxnQueries, item => item.PageSize == 200).Query;
        Assert.Equal(["client-a"], txnChartQuery.ClientMachines);
        Assert.Equal("drug-a", txnChartQuery.DrugId);
        Assert.Equal("10mg", txnChartQuery.Spec);

        var trendOverviewQuery = Assert.Single(repo.TrendQueries,
            item => item.PageSize == 10 && item.Query.TopN == 10).Query;
        Assert.Equal(["client-a"], trendOverviewQuery.ClientMachines);

        var txnOverviewQuery = Assert.Single(repo.TxnQueries,
            item => item.PageSize == 10 && item.Query.TopN == 10).Query;
        Assert.Equal(["client-a"], txnOverviewQuery.ClientMachines);
    }

    [Fact]
    public async Task Distribution_queries_are_skipped_when_scope_is_unchanged()
    {
        var repo = new Repo();
        var service = new DashboardService(repo);

        var snapshot = await service.GetSnapshotAsync(CreateRequest(refreshDistributions: false), CancellationToken.None);

        Assert.False(snapshot.DistributionsRefreshed);
        Assert.Single(repo.ClientQueries);
        Assert.Null(repo.EntryChartQuery);
        Assert.Empty(snapshot.ChartClients);
        Assert.Empty(snapshot.EntryChart);
    }

    [Fact]
    public async Task Client_names_are_skipped_when_not_requested()
    {
        var repo = new Repo();
        var service = new DashboardService(repo);

        var snapshot = await service.GetSnapshotAsync(
            CreateRequest(refreshDistributions: false, refreshClientNames: false),
            CancellationToken.None);

        Assert.Equal(0, repo.ClientNameCalls);
        Assert.Empty(snapshot.ClientNames);
    }

    private static DashboardRequest CreateRequest(
        bool refreshDistributions,
        bool refreshClientNames = true)
        => new(
            Filter: new DashboardFilter(
                new DateOnly(2026, 7, 1),
                new DateOnly(2026, 7, 16),
                ["client-a"],
                " drug-a ",
                " 10mg ",
                TrendMetric.Qty),
            RefreshDistributions: refreshDistributions,
            OverviewTopN: 10,
            EntryOverviewTopN: 6,
            TxnPageIndex: 1,
            TxnPageSize: 50,
            TxnTrendPageIndex: 1,
            TxnTrendPageSize: 50,
            EntryPageIndex: 1,
            EntryPageSize: 50,
            AbnormalPageIndex: 1,
            AbnormalPageSize: 50,
            RefreshClientNames: refreshClientNames);

    private sealed class Repo : IDashboardRepo
    {
        public int ClientNameCalls { get; private set; }
        public List<DashboardQuery> ClientQueries { get; } = [];
        public List<(DashboardQuery Query, int PageSize)> TrendQueries { get; } = [];
        public List<(DashboardQuery Query, int PageSize)> TxnQueries { get; } = [];
        public DashboardQuery? EntryChartQuery { get; private set; }

        public Task<IReadOnlyList<string>> GetClientNamesAsync(CancellationToken ct)
        {
            ClientNameCalls++;
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        public Task<IReadOnlyList<(string Client, long Value)>> GetClientsAsync(DashboardQuery q, CancellationToken ct)
        {
            ClientQueries.Add(q);
            return Task.FromResult<IReadOnlyList<(string Client, long Value)>>([]);
        }

        public Task<DashboardKpiDto> GetKpisAsync(DashboardQuery q, CancellationToken ct)
            => Task.FromResult(new DashboardKpiDto(0, 0, 0, 0, 0, 0, 0, null, null));

        public Task<PagedResult<TrendRowDto>> GetTrendPageAsync(
            DashboardQuery q, int page, int pageSize, CancellationToken ct)
        {
            TrendQueries.Add((q, pageSize));
            return Task.FromResult(new PagedResult<TrendRowDto>([], 0));
        }

        public Task<PagedResult<TraceTxnDto>> GetRecentTxnsPageAsync(
            DashboardQuery q, int page, int pageSize, CancellationToken ct)
        {
            TxnQueries.Add((q, pageSize));
            return Task.FromResult(new PagedResult<TraceTxnDto>([], 0));
        }

        public Task<PagedResult<AbnormalRowDto>> GetAbnormalQueuePageAsync(
            DashboardQuery q, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(new PagedResult<AbnormalRowDto>([], 0));

        public Task<PagedResult<TraceEntryLogDto>> GetEntryLogsPageAsync(
            DashboardQuery q, int page, int pageSize, CancellationToken ct)
            => Task.FromResult(new PagedResult<TraceEntryLogDto>([], 0));

        public Task<IReadOnlyList<EntryChartRowDto>> GetEntryChartAsync(DashboardQuery q, CancellationToken ct)
        {
            EntryChartQuery = q;
            return Task.FromResult<IReadOnlyList<EntryChartRowDto>>([]);
        }

        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
