using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class ApiScanCodeTests
{
    [Fact]
    public async Task SubmitAsync_posts_command()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(
            HttpStatusCode.OK,
            """
            {
              "drugFound": true,
              "insert": { "requestedCount": 1, "insertedCount": 1, "skippedCount": 0 },
              "qtyPerTrace": 1,
              "entryResult": "success",
              "entryMessage": "ok"
            }
            """);

        using var client = CreateClient(token, api);
        var scan = new ApiScanCode(client);
        var result = await scan.SubmitAsync(
            new ScanCodeSubmitRequest(
                "d1",
                "s1",
                ["T1"],
                new CodeAnalysis(1, 0, 0, 0, ["T1"]),
                "c1"),
            TestContext.Current.CancellationToken);

        Assert.True(result.DrugFound);
        Assert.Equal(1, result.Insert.InsertedCount);
        Assert.Equal(HttpMethod.Post, Assert.Single(api.Calls).Method);
        Assert.EndsWith("/v1/trace-codes/submit", api.Calls[0].Uri.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindExisting_posts_check_existing()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(HttpStatusCode.OK, """{"existing":["T1"]}""");

        using var client = CreateClient(token, api);
        var scan = new ApiScanCode(client);
        var existing = await scan.FindExistingTraceCodesAsync(
            ["T1", "T2"],
            TestContext.Current.CancellationToken);

        Assert.Equal(["T1"], existing);
        Assert.EndsWith("/v1/trace-codes/check-existing", api.Calls[0].Uri.AbsolutePath, StringComparison.Ordinal);
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
        public string CurrentLogPath => "/tmp/api-scan-code-test.log";

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
