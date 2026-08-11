using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class DashboardEndpointsTests
{
    public static TheoryData<string> DashboardRoutes =>
    [
        "/v1/dashboard/snapshot?from=2024-01-01&to=2024-01-07",
        "/v1/dashboard/transactions?from=2024-01-01&to=2024-01-07&page=1&pageSize=20",
        "/v1/dashboard/trends?from=2024-01-01&to=2024-01-07&page=1&pageSize=20",
        "/v1/dashboard/entries?from=2024-01-01&to=2024-01-07&page=1&pageSize=20",
        "/v1/dashboard/abnormal?from=2024-01-01&to=2024-01-07&page=1&pageSize=20",
        "/v1/dashboard/drug-ids",
        "/v1/dashboard/drugs/d1/specs",
    ];

    [Theory]
    [MemberData(nameof(DashboardRoutes))]
    public async Task Dashboard_routes_reject_anonymous(string path)
    {
        var dashboard = new FakeDashboardService();
        await using var factory = new ApiFactory { Dashboard = dashboard };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(DashboardRoutes))]
    public async Task Dashboard_routes_accept_bearer(string path)
    {
        var dashboard = new FakeDashboardService();
        await using var factory = new ApiFactory { Dashboard = dashboard };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Snapshot_returns_mapped_clients()
    {
        var dashboard = new FakeDashboardService();
        await using var factory = new ApiFactory { Dashboard = dashboard };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            "/v1/dashboard/snapshot?from=2024-01-01&to=2024-01-07&clientMachine=pc-a&overviewTopN=3",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("c1", doc.RootElement.GetProperty("clientNames")[0].GetString());
        Assert.Equal("pc-a", doc.RootElement.GetProperty("topClients")[0].GetProperty("client").GetString());
        Assert.Equal(9, doc.RootElement.GetProperty("topClients")[0].GetProperty("value").GetInt64());
        Assert.Equal(3, dashboard.LastOverviewTopN);
        Assert.Equal(["pc-a"], dashboard.LastFilter?.ClientMachines);
    }

    [Fact]
    public async Task Snapshot_rejects_inverted_range()
    {
        var dashboard = new FakeDashboardService();
        await using var factory = new ApiFactory { Dashboard = dashboard };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            "/v1/dashboard/snapshot?from=2024-01-10&to=2024-01-01",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("overviewTopN", "201")]
    [InlineData("txnPageIndex", "10001")]
    [InlineData("txnPageSize", "201")]
    public async Task Snapshot_rejects_out_of_range_bounds(string name, string value)
    {
        var dashboard = new FakeDashboardService();
        await using var factory = new ApiFactory { Dashboard = dashboard };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            $"/v1/dashboard/snapshot?from=2024-01-01&to=2024-01-07&{name}={value}",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/v1/dashboard/transactions")]
    [InlineData("/v1/dashboard/trends")]
    [InlineData("/v1/dashboard/entries")]
    [InlineData("/v1/dashboard/abnormal")]
    public async Task Page_routes_return_total_count(string path)
    {
        var dashboard = new FakeDashboardService();
        await using var factory = new ApiFactory { Dashboard = dashboard };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync(
            $"{path}?from=2024-01-01&to=2024-01-07&page=2&pageSize=20",
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(1, doc.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, dashboard.LastPage);
        Assert.Equal(20, dashboard.LastPageSize);
    }

    [Fact]
    public async Task Drug_ids_and_specs_return_items()
    {
        var dashboard = new FakeDashboardService();
        await using var factory = new ApiFactory { Dashboard = dashboard };
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var drugsResponse = await client.GetAsync(
            "/v1/dashboard/drug-ids",
            TestContext.Current.CancellationToken);
        var drugsBody = await drugsResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, drugsResponse.StatusCode);
        using (var doc = JsonDocument.Parse(drugsBody))
        {
            Assert.Equal("d1", doc.RootElement.GetProperty("items")[0].GetString());
        }

        using var specsResponse = await client.GetAsync(
            "/v1/dashboard/drugs/d1/specs",
            TestContext.Current.CancellationToken);
        var specsBody = await specsResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, specsResponse.StatusCode);
        using (var doc = JsonDocument.Parse(specsBody))
        {
            Assert.Equal("s1", doc.RootElement.GetProperty("items")[0].GetString());
        }
    }

    private sealed class FakeDashboardService : IDashboardService
    {
        public DashboardFilter? LastFilter { get; private set; }

        public int LastOverviewTopN { get; private set; }

        public int LastPage { get; private set; }

        public int LastPageSize { get; private set; }

        public Task<DashboardSnapshot> GetSnapshotAsync(DashboardRequest request, CancellationToken ct)
        {
            LastFilter = request.Filter;
            LastOverviewTopN = request.OverviewTopN;
            var emptyTxns = new PagedResult<TraceTxnDto>([], 0);
            var emptyTrends = new PagedResult<TrendRowDto>([], 0);
            var emptyEntries = new PagedResult<TraceEntryLogDto>([], 0);
            var emptyAbnormal = new PagedResult<AbnormalRowDto>([], 0);
            return Task.FromResult(
                new DashboardSnapshot(
                    ClientNames: ["c1"],
                    Kpi: new DashboardKpiDto(0, 0, 0, 0, 0, 0, 0, null, null),
                    Trend: [],
                    TxnsOverview: emptyTxns,
                    TxnsPage: emptyTxns,
                    TxnTrendPage: emptyTrends,
                    EntriesOverview: emptyEntries,
                    EntriesPage: emptyEntries,
                    TopClients: [("pc-a", 9)],
                    ChartTrend: [],
                    ChartTxns: [],
                    DistributionsRefreshed: request.RefreshDistributions,
                    ChartClients: [],
                    EntryChart: [],
                    Abnormal: emptyAbnormal));
        }

        public Task<PagedResult<TraceTxnDto>> GetTxnPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            TrackPage(filter, page, pageSize);
            return Task.FromResult(
                new PagedResult<TraceTxnDto>(
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
                            null),
                    ],
                    1));
        }

        public Task<PagedResult<TrendRowDto>> GetTxnTrendPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            TrackPage(filter, page, pageSize);
            return Task.FromResult(
                new PagedResult<TrendRowDto>(
                    [new TrendRowDto(1, "n", "s", null, 0m, "1")],
                    1));
        }

        public Task<PagedResult<TraceEntryLogDto>> GetEntryPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            TrackPage(filter, page, pageSize);
            return Task.FromResult(
                new PagedResult<TraceEntryLogDto>(
                    [
                        new TraceEntryLogDto(
                            DateTimeOffset.UnixEpoch,
                            "d",
                            "s",
                            1,
                            1,
                            1,
                            0,
                            "ok",
                            null,
                            "c",
                            "src",
                            null),
                    ],
                    1));
        }

        public Task<PagedResult<AbnormalRowDto>> GetAbnormalPageAsync(
            DashboardFilter filter,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            TrackPage(filter, page, pageSize);
            return Task.FromResult(
                new PagedResult<AbnormalRowDto>(
                    [new AbnormalRowDto("t", "d", "c", TxnBadge.Warning, 1)],
                    1));
        }

        public Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(["d1"]);

        public Task<IReadOnlyList<string>> GetSpecsByDrugAsync(string drugId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(["s1"]);

        private void TrackPage(DashboardFilter filter, int page, int pageSize)
        {
            LastFilter = filter;
            LastPage = page;
            LastPageSize = pageSize;
        }
    }
}
