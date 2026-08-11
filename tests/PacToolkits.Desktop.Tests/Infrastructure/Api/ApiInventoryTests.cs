using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class ApiInventoryTests
{
    [Fact]
    public async Task ApplyStockRowEditsAsync_posts_edit()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(
            HttpStatusCode.OK,
            """
            {
              "savedCount": 1,
              "failedCount": 0,
              "lastError": null,
              "saved": [
                { "matchTraceCode": "T1", "newVersion": 2, "newTraceCode": null, "newRemain": 3 }
              ],
              "conflicts": []
            }
            """);

        using var client = CreateClient(token, api);
        var inventory = new ApiInventory(client);
        var result = await inventory.ApplyStockRowEditsAsync(
            [new StockRowEditRequest("T1", 1, null, 3)],
            new TraceCodeValidationRule(2, string.Empty),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SavedCount);
        Assert.Equal(HttpMethod.Post, Assert.Single(api.Calls).Method);
        Assert.EndsWith("/v1/inventory/stock/edit", api.Calls[0].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyStockRowEditsAsync_maps_409_problem_conflicts()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(
            HttpStatusCode.Conflict,
            """
            {
              "status": 409,
              "code": "conflict",
              "title": "Concurrency conflict",
              "detail": "One or more stock rows changed",
              "traceId": "t1",
              "conflicts": [
                {
                  "matchTraceCode": "T1",
                  "newTraceCode": null,
                  "newRemain": 3,
                  "current": {
                    "drugId": "d1",
                    "spec": "s1",
                    "traceCode": "T1",
                    "qty": 1,
                    "remain": 1,
                    "status": 0,
                    "version": 9,
                    "isLow": false
                  }
                }
              ]
            }
            """,
            contentType: "application/problem+json");

        using var client = CreateClient(token, api);
        var inventory = new ApiInventory(client);
        var result = await inventory.ApplyStockRowEditsAsync(
            [new StockRowEditRequest("T1", 1, null, 3)],
            new TraceCodeValidationRule(2, string.Empty),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.SavedCount);
        Assert.Equal("One or more stock rows changed", result.LastError);
        Assert.Single(result.Conflicts);
        Assert.Equal("T1", result.Conflicts[0].MatchTraceCode);
        Assert.Equal(9, result.Conflicts[0].Current!.Version);
    }

    [Fact]
    public async Task ApplyStockRowEditsAsync_rethrows_payload_mismatch_409()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(
            HttpStatusCode.Conflict,
            """
            {
              "status": 409,
              "code": "payload_mismatch",
              "title": "Command payload mismatch",
              "detail": "Same CommandId with a different request body",
              "traceId": "t1"
            }
            """,
            contentType: "application/problem+json");

        using var client = CreateClient(token, api);
        var inventory = new ApiInventory(client);
        var ex = await Assert.ThrowsAsync<PacApiConflictException>(() =>
            inventory.ApplyStockRowEditsAsync(
                [new StockRowEditRequest("T1", 1, null, 3)],
                new TraceCodeValidationRule(2, string.Empty),
                TestContext.Current.CancellationToken));

        Assert.Equal("payload_mismatch", ex.Code);
        Assert.Null(ex.Conflicts);
    }

    [Fact]
    public async Task TargetDrugSpecExistsAsync_gets_query()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(HttpStatusCode.OK, """{"exists":true}""");

        using var client = CreateClient(token, api);
        var inventory = new ApiInventory(client);
        var exists = await inventory.TargetDrugSpecExistsAsync("d1", "s1", TestContext.Current.CancellationToken);

        Assert.True(exists);
        Assert.Contains("target-exists", api.Calls[0].Uri.AbsolutePath, StringComparison.Ordinal);
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

        public void EnqueueJson(HttpStatusCode status, string json, string contentType = "application/json")
            => Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, contentType),
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
        public string CurrentLogPath => "/tmp/api-inventory-test.log";

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
