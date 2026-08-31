using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class ApiLookupCatalogTests
{
    [Fact]
    public async Task Catalog_reads_drug_ids_and_specs()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(HttpStatusCode.OK, """{"items":["d1","d2"]}""");
        api.EnqueueJson(HttpStatusCode.OK, """{"items":["s1"]}""");

        using var client = CreateClient(token, api);
        var lookup = new ApiLookupCatalog(client);

        var drugs = await lookup.GetDrugIdsAsync(TestContext.Current.CancellationToken);
        var specs = await lookup.GetSpecsByDrugAsync("d1", TestContext.Current.CancellationToken);

        Assert.Equal(["d1", "d2"], drugs);
        Assert.Equal(["s1"], specs);
        Assert.Equal(2, api.Calls.Count);
        Assert.EndsWith("/v1/catalog/drug-ids", api.Calls[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/v1/catalog/drugs/specs", api.Calls[1].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("drugId=d1", api.Calls[1].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetQtyAsync_puts_slash_key_in_query()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(HttpStatusCode.OK, """{"quantity":12}""");

        using var client = CreateClient(token, api);
        var lookup = new ApiLookupCatalog(client);
        var qty = await lookup.GetQtyAsync("氨/西林", "10ml/盒", TestContext.Current.CancellationToken);

        Assert.Equal(12, qty);
        Assert.Single(api.Calls);
        Assert.Equal("/v1/catalog/drugs/quantity", api.Calls[0].Uri.AbsolutePath);
        Assert.Contains(
            "drugId=" + Uri.EscapeDataString("氨/西林"),
            api.Calls[0].Uri.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "spec=" + Uri.EscapeDataString("10ml/盒"),
            api.Calls[0].Uri.Query,
            StringComparison.Ordinal);
    }

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
        public string CurrentLogPath => "/tmp/api-lookup-catalog-test.log";

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
