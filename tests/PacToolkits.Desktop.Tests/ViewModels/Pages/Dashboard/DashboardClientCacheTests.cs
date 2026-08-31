using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class DashboardClientCacheTests
{
    [Fact]
    public async Task Silent_reload_refreshes_client_names()
    {
        var dashboard = new FakeDashboardService();
        var page = new Dashboard(dashboard);
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());

        await page.TestRunReloadCoreAsync();
        Assert.True(dashboard.Requests[0].RefreshClientNames);

        using (page.TestBeginSilentReload())
        {
            await page.TestRunReloadCoreAsync();
        }

        Assert.Equal(2, dashboard.Requests.Count);
        Assert.True(dashboard.Requests[1].RefreshClientNames);
        Assert.True(dashboard.Requests[1].RefreshDistributions);
    }

    [Fact]
    public async Task Alias_change_refreshes_client_names_and_distributions()
    {
        var dashboard = new FakeDashboardService();
        var alias = new CapturingAlias();
        var page = new Dashboard(dashboard, clientAlias: alias);
        page.TestInjectServices(apiAvailability: AppPageBaseReloadPipelineTests.FakeApiAvailability.Ready());

        await page.TestRunReloadCoreAsync();
        Assert.Equal("pc-a", Assert.Single(page.Clients, item => item.Raw == "pc-a").Display);
        page.SelectedClient = Assert.Single(page.Clients, item => item.Raw == "pc-a");
        await page.TestRunReloadCoreAsync();
        Assert.False(dashboard.Requests[1].RefreshClientNames);
        Assert.False(dashboard.Requests[1].RefreshDistributions);

        alias.Set("pc-a", "前台");
        alias.Fire();
        await page.TestRunReloadCoreAsync();

        Assert.Equal(3, dashboard.Requests.Count);
        Assert.True(dashboard.Requests[2].RefreshClientNames);
        Assert.True(dashboard.Requests[2].RefreshDistributions);
        Assert.Equal("前台", Assert.Single(page.Clients, item => item.Raw == "pc-a").Display);
    }

    private sealed class CapturingAlias : IClientAliasService
    {
        private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

        public event Action? Changed;

        public void Set(string machine, string display) => _aliases[machine] = display;

        public void Fire() => Changed?.Invoke();

        public IReadOnlyDictionary<string, string> GetAll() => _aliases;

        public string Resolve(string? machine)
        {
            if (string.IsNullOrWhiteSpace(machine))
            {
                return string.Empty;
            }

            return _aliases.TryGetValue(machine, out var display) ? display : machine;
        }

        public void ReplaceAll(IEnumerable<KeyValuePair<string, string>> items)
        {
        }

        public void Apply(IReadOnlyDictionary<string, string> aliases)
        {
        }

        public void Reload()
        {
        }
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
                    ChartClients: request.RefreshDistributions ? [("pc-a", 3L)] : [],
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
