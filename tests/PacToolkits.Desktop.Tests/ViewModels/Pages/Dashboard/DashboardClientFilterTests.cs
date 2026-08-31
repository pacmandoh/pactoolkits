using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class DashboardClientFilterTests
{
    [Fact]
    public async Task Client_filter_does_not_refresh_client_chart_distribution()
    {
        var dashboard = new FakeDashboardService();
        var page = new Dashboard(dashboard);
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());

        await page.TestRunReloadCoreAsync();

        Assert.True(Assert.Single(dashboard.Requests).RefreshDistributions);
        Assert.True(dashboard.Requests[0].RefreshClientNames);
        Assert.Single(page.RecentTxnsOverview);
        Assert.False(page.IsTxnOverviewEmpty);
        Assert.Equal(2, page.ChartClients.Count);
        var chartClients = page.ChartClients.ToArray();
        var client = Assert.Single(page.Clients, item => item.Raw == "pc-a");

        page.SelectedClient = client;
        await page.TestRunReloadCoreAsync();

        Assert.Equal(2, dashboard.Requests.Count);
        Assert.False(dashboard.Requests[1].RefreshDistributions);
        Assert.False(dashboard.Requests[1].RefreshClientNames);
        Assert.Equal(["pc-a"], dashboard.Requests[1].Filter.ClientMachines);
        Assert.Contains(page.Clients, item => item.Raw == "pc-a");
        Assert.Contains(page.Clients, item => item.Raw == "pc-b");
        Assert.Same(chartClients[0], page.ChartClients[0]);
        Assert.Same(chartClients[1], page.ChartClients[1]);
    }

    private sealed class FakeDashboardService : IDashboardService
    {
        public List<DashboardRequest> Requests { get; } = [];

        public Task<DashboardSnapshot> GetSnapshotAsync(DashboardRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var emptyTxns = new PagedResult<TraceTxnDto>([], 0);
            var emptyTrends = new PagedResult<TrendRowDto>([], 0);
            var emptyEntries = new PagedResult<TraceEntryLogDto>([], 0);
            var emptyAbnormal = new PagedResult<AbnormalRowDto>([], 0);
            var overviewTxns = new PagedResult<TraceTxnDto>(
                [
                    new TraceTxnDto(
                        1,
                        TxnStatus.Commit,
                        TxnBadge.Done,
                        "t",
                        "d",
                        "s",
                        1,
                        DateTimeOffset.UnixEpoch,
                        "pc-a"),
                ],
                1);
            IReadOnlyList<(string Client, long Value)> chartClients = request.RefreshDistributions
                ? [("pc-a", 3L), ("pc-b", 1L)]
                : [];
            return Task.FromResult(
                new DashboardSnapshot(
                    ClientNames: request.RefreshClientNames ? ["pc-a", "pc-b"] : [],
                    Kpi: new DashboardKpiDto(0, 0, 0, 0, 0, 0, 0, null, null),
                    Trend: [],
                    TxnsOverview: overviewTxns,
                    TxnsPage: emptyTxns,
                    TxnTrendPage: emptyTrends,
                    EntriesOverview: emptyEntries,
                    EntriesPage: emptyEntries,
                    TopClients: [("pc-a", 3L)],
                    ChartTrend: [],
                    ChartTxns: [],
                    DistributionsRefreshed: request.RefreshDistributions,
                    ChartClients: chartClients,
                    EntryChart: [],
                    Abnormal: emptyAbnormal));
        }

        public Task<PagedResult<TraceTxnDto>> GetTxnPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
            => Task.FromResult(new PagedResult<TraceTxnDto>([], 0));

        public Task<PagedResult<TrendRowDto>> GetTxnTrendPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
            => Task.FromResult(new PagedResult<TrendRowDto>([], 0));

        public Task<PagedResult<TraceEntryLogDto>> GetEntryPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
            => Task.FromResult(new PagedResult<TraceEntryLogDto>([], 0));

        public Task<PagedResult<AbnormalRowDto>> GetAbnormalPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
            => Task.FromResult(new PagedResult<AbnormalRowDto>([], 0));
    }
}
