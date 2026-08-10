using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Tests;

public sealed class PacApiClientTests
{
    [Fact]
    public async Task SendAsync_exchanges_token_then_sends_bearer()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(
            HttpStatusCode.OK,
            """{"accessToken":"tok-1","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""");
        api.EnqueueJson(HttpStatusCode.OK, """{"ok":true}""");

        using var client = CreateClient(token, api, sse);
        using var response = await client.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/ping")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokenCall = Assert.Single(token.Calls);
        Assert.Equal(HttpMethod.Post, tokenCall.Method);
        Assert.EndsWith("/v1/auth/token", tokenCall.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("test-key", tokenCall.ApiKey);
        Assert.Null(tokenCall.Bearer);

        Assert.Equal("tok-1", Assert.Single(api.Calls).Bearer);
        Assert.Empty(sse.Calls);
    }

    [Fact]
    public async Task SendAsync_on_401_refreshes_token_and_retries()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(
            HttpStatusCode.OK,
            """{"accessToken":"tok-1","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""");
        token.EnqueueJson(
            HttpStatusCode.OK,
            """{"accessToken":"tok-2","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""");
        api.EnqueueStatus(HttpStatusCode.Unauthorized);
        api.EnqueueJson(HttpStatusCode.OK, """{"ok":true}""");

        using var client = CreateClient(token, api, sse);
        using var response = await client.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/ping")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, token.Calls.Count);
        Assert.Equal(2, api.Calls.Count);
        Assert.Equal("tok-1", api.Calls[0].Bearer);
        Assert.Equal("tok-2", api.Calls[1].Bearer);
    }

    [Fact]
    public async Task Timeouts_split_api_short_and_sse_infinite()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        using var client = CreateClient(token, api, sse);

        Assert.Equal(PacApiClient.ApiTimeout, client.TokenHttpTimeout);
        Assert.Equal(PacApiClient.ApiTimeout, client.ApiHttpTimeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.SseHttpTimeout);

        token.EnqueueJson(
            HttpStatusCode.OK,
            """{"accessToken":"tok-sse","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""");
        sse.EnqueueJson(HttpStatusCode.OK, """{"ok":true}""");

        using var response = await client.SendSseAsync(
            () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/changes/stream")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("tok-sse", Assert.Single(sse.Calls).Bearer);
        Assert.Empty(api.Calls);
    }

    private static PacApiClient CreateClient(
        ScriptedHandler token,
        ScriptedHandler api,
        ScriptedHandler sse)
        => new(
            "http://127.0.0.1:5080",
            "test-key",
            new NullLogger(),
            "X-Api-Key",
            token,
            api,
            sse);

    private sealed record Call(HttpMethod Method, Uri Uri, string? Bearer, string? ApiKey);

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<Call> Calls { get; } = [];

        public void EnqueueJson(HttpStatusCode status, string json)
            => _responses.Enqueue(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        public void EnqueueStatus(HttpStatusCode status)
            => _responses.Enqueue(_ => new HttpResponseMessage(status));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException($"unexpected request {request.Method} {request.RequestUri}");
            }

            string? bearer = null;
            if (request.Headers.Authorization is { Scheme: "Bearer" } auth)
            {
                bearer = auth.Parameter;
            }

            string? apiKey = null;
            if (request.Headers.TryGetValues("X-Api-Key", out var keys))
            {
                apiKey = keys.FirstOrDefault();
            }

            Calls.Add(new Call(
                request.Method,
                request.RequestUri ?? new Uri("http://invalid/"),
                bearer,
                apiKey));

            return Task.FromResult(_responses.Dequeue()(request));
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/pactoolkits-pac-api-client-test.log";
        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
