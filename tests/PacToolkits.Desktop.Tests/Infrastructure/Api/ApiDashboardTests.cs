using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class ApiDashboardTests
{
    [Fact]
    public async Task GetSnapshotAsync_maps_client_counts_and_query()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(
            HttpStatusCode.OK,
            """
            {
              "clientNames":["c1"],
              "kpi":{"availableRemain":1,"periodUsed":2,"abnormal":0,"lowStockCount":0,"totalQty":3,"totalTxnCount":4,"totalDrugCount":5,"selectedPoolCount":null,"selectedZeroRemainCount":null},
              "trend":[],
              "txnsOverview":{"rows":[],"totalCount":0},
              "txnsPage":{"rows":[],"totalCount":0},
              "txnTrendPage":{"rows":[],"totalCount":0},
              "entriesOverview":{"rows":[],"totalCount":0},
              "entriesPage":{"rows":[],"totalCount":0},
              "topClients":[{"client":"pc-a","value":9}],
              "chartTrend":[],
              "chartTxns":[],
              "distributionsRefreshed":true,
              "chartClients":[],
              "entryChart":[],
              "abnormal":{"rows":[],"totalCount":0}
            }
            """);

        using var client = CreateClient(token, api);
        var service = new ApiDashboard(client);
        var filter = new DashboardFilter(
            From: new DateOnly(2024, 1, 1),
            To: new DateOnly(2024, 1, 7),
            ClientMachines: ["pc-a"],
            DrugId: "d1",
            Spec: "s1",
            TrendMetric: TrendMetric.Txn);
        var request = new DashboardRequest(
            Filter: filter,
            RefreshDistributions: true,
            OverviewTopN: 10,
            EntryOverviewTopN: 6,
            TxnPageIndex: 1,
            TxnPageSize: 20,
            TxnTrendPageIndex: 1,
            TxnTrendPageSize: 20,
            EntryPageIndex: 1,
            EntryPageSize: 20,
            AbnormalPageIndex: 1,
            AbnormalPageSize: 20);

        var snapshot = await service.GetSnapshotAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(["c1"], snapshot.ClientNames);
        Assert.Equal(("pc-a", 9L), Assert.Single(snapshot.TopClients));
        Assert.True(snapshot.DistributionsRefreshed);
        var call = Assert.Single(api.Calls);
        Assert.Equal(HttpMethod.Get, call.Method);
        Assert.Contains("/v1/dashboard/snapshot?", call.Uri.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("from=2024-01-01", call.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("clientMachine=pc-a", call.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("drugId=d1", call.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("trendMetric=Txn", call.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("refreshDistributions=true", call.Uri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("transactions", nameof(IDashboardService.GetTxnPageAsync))]
    [InlineData("trends", nameof(IDashboardService.GetTxnTrendPageAsync))]
    [InlineData("entries", nameof(IDashboardService.GetEntryPageAsync))]
    [InlineData("abnormal", nameof(IDashboardService.GetAbnormalPageAsync))]
    public async Task Page_methods_hit_routes_and_deserialize(string segment, string methodName)
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(HttpStatusCode.OK, PageJson(segment));

        using var client = CreateClient(token, api);
        var service = new ApiDashboard(client);
        var filter = new DashboardFilter(
            From: new DateOnly(2024, 2, 1),
            To: new DateOnly(2024, 2, 2),
            ClientMachines: null,
            DrugId: null,
            Spec: null,
            TrendMetric: TrendMetric.Qty);
        var ct = TestContext.Current.CancellationToken;

        object page = methodName switch
        {
            nameof(IDashboardService.GetTxnPageAsync)
                => await service.GetTxnPageAsync(filter, 2, 50, ct),
            nameof(IDashboardService.GetTxnTrendPageAsync)
                => await service.GetTxnTrendPageAsync(filter, 2, 50, ct),
            nameof(IDashboardService.GetEntryPageAsync)
                => await service.GetEntryPageAsync(filter, 2, 50, ct),
            nameof(IDashboardService.GetAbnormalPageAsync)
                => await service.GetAbnormalPageAsync(filter, 2, 50, ct),
            _ => throw new InvalidOperationException(methodName),
        };

        Assert.Equal(1, (int)page.GetType().GetProperty("TotalCount")!.GetValue(page)!);
        var call = Assert.Single(api.Calls);
        Assert.Contains($"/v1/dashboard/{segment}?", call.Uri.PathAndQuery, StringComparison.Ordinal);
        Assert.Contains("page=2", call.Uri.Query, StringComparison.Ordinal);
        Assert.Contains("pageSize=50", call.Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_methods_hit_drug_routes()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(HttpStatusCode.OK, """{"items":["d1","d2"]}""");
        api.EnqueueJson(HttpStatusCode.OK, """{"items":["s1"]}""");

        using var client = CreateClient(token, api);
        var service = new ApiDashboard(client);

        var drugs = await service.GetDrugIdsAsync(TestContext.Current.CancellationToken);
        var specs = await service.GetSpecsByDrugAsync("d1", TestContext.Current.CancellationToken);

        Assert.Equal(["d1", "d2"], drugs);
        Assert.Equal(["s1"], specs);
        Assert.Equal(2, api.Calls.Count);
        Assert.EndsWith("/v1/dashboard/drug-ids", api.Calls[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/v1/dashboard/drugs/d1/specs", api.Calls[1].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    private static string PageJson(string segment)
        => segment switch
        {
            "transactions" => """
                {"rows":[{"id":1,"status":1,"badge":1,"title":"t","drugId":"d","spec":"s","qty":1,"createdAt":"2024-01-01T00:00:00Z","clientRaw":null}],"totalCount":1}
                """,
            "trends" => """
                {"rows":[{"rank":1,"name":"n","sub":"s","topClientRaw":null,"topClientPct":0,"valueText":"1"}],"totalCount":1}
                """,
            "entries" => """
                {"rows":[{"entryAt":"2024-01-01T00:00:00Z","drugId":"d","spec":"s","entryCount":1,"qtyPerTrace":1,"totalAvailableQty":1,"failedCount":0,"result":"ok","txnId":null,"client":"c","source":"src","message":null}],"totalCount":1}
                """,
            "abnormal" => """
                {"rows":[{"title":"t","detail":"d","clientRaw":"c","badge":2,"txnId":1}],"totalCount":1}
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null),
        };

    private static PacApiClient CreateClient(HttpMessageHandler token, HttpMessageHandler api)
        => new(
            "http://127.0.0.1:5080",
            "test-key",
            new NullLogger(),
            "X-Api-Key",
            token,
            api,
            new ScriptedHandler());

    private sealed record Call(HttpMethod Method, Uri Uri);

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<Call> Calls { get; } = [];

        public void EnqueueToken(string accessToken)
            => EnqueueJson(
                HttpStatusCode.OK,
                $$"""{"accessToken":"{{accessToken}}","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""");

        public void EnqueueJson(HttpStatusCode status, string json)
            => Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        public void Enqueue(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            lock (_gate)
            {
                _responses.Enqueue(factory);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_responses.Count == 0)
                {
                    throw new InvalidOperationException($"unexpected request {request.Method} {request.RequestUri}");
                }

                Calls.Add(new Call(request.Method, request.RequestUri ?? new Uri("http://invalid/")));
                return Task.FromResult(_responses.Dequeue()(request));
            }
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/dashboard-api-service-test.log";

        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null)
        {
        }

        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null)
        {
        }

        public void Warn(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Error(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Fatal(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
