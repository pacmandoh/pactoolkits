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

    [Fact]
    public async Task Invalid_token_response_does_not_publish_partial_state()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(
            HttpStatusCode.OK,
            """{"accessToken":"tok-1","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""");
        token.EnqueueJson(
            HttpStatusCode.OK,
            """{"accessToken":"bad","tokenType":"Bearer","expiresIn":0,"clientId":"c1"}""");
        api.EnqueueJson(HttpStatusCode.OK, """{"ok":true}""");
        api.EnqueueStatus(HttpStatusCode.Unauthorized);

        using var client = CreateClient(token, api, sse);
        using (var warm = await client.SendAsync(
                   () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/ping")),
                   TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        }

        Assert.Equal("tok-1", client.CurrentAccessToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/ping")),
                TestContext.Current.CancellationToken));

        // 非法 expiresIn 不得污染缓存；仍保留换票前的有效票
        Assert.Equal("tok-1", client.CurrentAccessToken);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public async Task Short_expiresIn_reuses_token_across_consecutive_requests(int expiresIn)
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(
            HttpStatusCode.OK,
            $$"""{"accessToken":"tok-short","tokenType":"Bearer","expiresIn":{{expiresIn}},"clientId":"c1"}""");
        api.EnqueueJson(HttpStatusCode.OK, """{"ok":true}""");
        api.EnqueueJson(HttpStatusCode.OK, """{"ok":true}""");
        api.EnqueueJson(HttpStatusCode.OK, """{"ok":true}""");

        using var client = CreateClient(token, api, sse);
        var ct = TestContext.Current.CancellationToken;

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/ping")),
                ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Single(token.Calls);
        Assert.Equal(3, api.Calls.Count);
        Assert.All(api.Calls, c => Assert.Equal("tok-short", c.Bearer));
    }

    [Theory]
    [InlineData(30, 3)]
    [InlineData(60, 6)]
    [InlineData(120, 12)]
    [InlineData(3600, 60)]
    public void ComputeRefreshAt_uses_ten_percent_early_capped_at_one_minute(
        int expiresIn,
        int earlySeconds)
    {
        var issued = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var refreshAt = PacApiClient.ComputeRefreshAt(issued, expiresIn);

        Assert.Equal(issued.AddSeconds(expiresIn - earlySeconds), refreshAt);
    }

    [Fact]
    public async Task Concurrent_401_refreshes_token_once()
    {
        var token = new ScriptedHandler();
        var api = new ConcurrentStaleBearerHandler("tok-1");
        var sse = new ScriptedHandler();
        token.EnqueueJson(
            HttpStatusCode.OK,
            """{"accessToken":"tok-1","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""");
        token.EnqueueJson(
            HttpStatusCode.OK,
            """{"accessToken":"tok-2","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""");

        using var client = CreateClient(token, api, sse);
        var ct = TestContext.Current.CancellationToken;

        // 先拿到 tok-1，再并发打满三路 401，避免冷启动与换票交错
        api.WarmupRemaining = 1;
        using (var warm = await client.SendAsync(
                   () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/ping")),
                   ct))
        {
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        }

        var tasks = Enumerable.Range(0, 3)
            .Select(_ => client.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/ping")),
                ct))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Dispose();
        }

        // 预热换票 1 次 + 并发 401 共享换票 1 次；不是 1+3
        Assert.Equal(2, token.Calls.Count);
        Assert.Equal(4, api.Calls.Count(c => c.Bearer == "tok-1"));
        Assert.Equal(3, api.Calls.Count(c => c.Bearer == "tok-2"));
    }

    private static PacApiClient CreateClient(
        HttpMessageHandler token,
        HttpMessageHandler api,
        HttpMessageHandler sse)
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
        private readonly object _gate = new();
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<Call> Calls { get; } = [];

        public void EnqueueJson(HttpStatusCode status, string json)
        {
            lock (_gate)
            {
                _responses.Enqueue(_ => new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }
        }

        public void EnqueueStatus(HttpStatusCode status)
        {
            lock (_gate)
            {
                _responses.Enqueue(_ => new HttpResponseMessage(status));
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
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

            lock (_gate)
            {
                if (_responses.Count == 0)
                {
                    throw new InvalidOperationException($"unexpected request {request.Method} {request.RequestUri}");
                }

                Calls.Add(new Call(
                    request.Method,
                    request.RequestUri ?? new Uri("http://invalid/"),
                    bearer,
                    apiKey));
                return Task.FromResult(_responses.Dequeue()(request));
            }
        }
    }

    // 预热放行；其后旧票凑齐 3 路再一齐 401，新票直接 200
    private sealed class ConcurrentStaleBearerHandler(string staleBearer) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _staleBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _staleHits;

        public int WarmupRemaining { get; set; }

        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? bearer = null;
            if (request.Headers.Authorization is { Scheme: "Bearer" } auth)
            {
                bearer = auth.Parameter;
            }

            var uri = request.RequestUri ?? new Uri("http://invalid/");
            if (WarmupRemaining > 0)
            {
                WarmupRemaining--;
                Record(request.Method, uri, bearer);
                return Ok();
            }

            if (string.Equals(bearer, staleBearer, StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _staleHits) >= 3)
                {
                    _staleBarrier.TrySetResult();
                }

                await _staleBarrier.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                Record(request.Method, uri, bearer);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            Record(request.Method, uri, bearer);
            return Ok();
        }

        private void Record(HttpMethod method, Uri uri, string? bearer)
        {
            lock (Calls)
            {
                Calls.Add(new Call(method, uri, bearer, ApiKey: null));
            }
        }

        private static HttpResponseMessage Ok()
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json"),
            };
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
