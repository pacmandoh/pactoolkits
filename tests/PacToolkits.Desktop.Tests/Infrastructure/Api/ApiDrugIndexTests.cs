using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class ApiDrugIndexTests
{
    [Fact]
    public async Task SaveAsync_maps_concurrency_outcome()
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
              "detail": "Row changed",
              "traceId": "t1",
              "currentVersion": 9
            }
            """);
        api.EnqueueJson(
            HttpStatusCode.OK,
            """
            {
              "drugId": "d1",
              "spec": "s1",
              "qty": 1,
              "ruleKey": null,
              "preTc": null,
              "note": null,
              "createdAt": "2024-01-01T00:00:00Z",
              "updatedAt": null,
              "version": 9
            }
            """);

        using var client = CreateClient(token, api);
        var drugs = new ApiDrugIndex(client);
        var result = await drugs.SaveAsync(
            new DrugIndexSaveRequest(
                Dto: new DrugIndexDto(
                    "d1",
                    "s1",
                    1,
                    null,
                    null,
                    null,
                    DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                    null,
                    1),
                OriginDrugId: "d1",
                OriginSpec: "s1",
                ExpectedVersion: 1,
                IsNew: false,
                HasPrimaryKeyChanges: false,
                HasQtyChanged: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(DrugSaveOutcome.ConcurrencyConflict, result.Outcome);
        Assert.NotNull(result.Saved);
        Assert.Equal(9, result.Saved.Version);
        Assert.NotNull(result.Concurrency);
        Assert.Equal(2, api.Calls.Count);
        Assert.Equal(HttpMethod.Put, api.Calls[0].Method);
        Assert.Equal(HttpMethod.Get, api.Calls[1].Method);
    }

    [Fact]
    public async Task SaveAsync_rethrows_payload_mismatch_409()
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
            """);

        using var client = CreateClient(token, api);
        var drugs = new ApiDrugIndex(client);
        var ex = await Assert.ThrowsAsync<PacApiConflictException>(() =>
            drugs.SaveAsync(
                new DrugIndexSaveRequest(
                    Dto: new DrugIndexDto(
                        "d1",
                        "s1",
                        1,
                        null,
                        null,
                        null,
                        DateTimeOffset.Parse("2024-01-01T00:00:00Z"),
                        null,
                        1),
                    OriginDrugId: "d1",
                    OriginSpec: "s1",
                    ExpectedVersion: 1,
                    IsNew: false,
                    HasPrimaryKeyChanges: false,
                    HasQtyChanged: false),
                TestContext.Current.CancellationToken));

        Assert.Equal("payload_mismatch", ex.Code);
        Assert.Single(api.Calls);
        Assert.Equal(HttpMethod.Put, api.Calls[0].Method);
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
        public string CurrentLogPath => "/tmp/api-drug-index-test.log";

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
