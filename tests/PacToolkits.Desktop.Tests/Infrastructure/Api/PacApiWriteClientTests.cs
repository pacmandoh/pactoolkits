using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class PacApiWriteClientTests
{
    [Fact]
    public void ApplyCommandId_explicit_id_overwrites_existing_header()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1/v1/demo");
        request.Headers.TryAddWithoutValidation(PacApiHeaders.CommandId, Guid.NewGuid().ToString("D"));
        var forced = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        var applied = PacApiClient.ApplyCommandId(request, forced);

        Assert.Equal(forced, applied);
        Assert.True(request.Headers.TryGetValues(PacApiHeaders.CommandId, out var values));
        Assert.Equal(forced.ToString("D"), Assert.Single(values));
    }

    [Fact]
    public void ApplyCommandId_rejects_empty_guid()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1/v1/demo");

        var ex = Assert.Throws<ArgumentException>(() => PacApiClient.ApplyCommandId(request, Guid.Empty));

        Assert.Equal("commandId", ex.ParamName);
        Assert.False(request.Headers.Contains(PacApiHeaders.CommandId));
    }

    [Fact]
    public async Task PostJsonAsync_rejects_empty_command_id()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");

        using var client = CreateClient(token, api);
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => client.PostJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, client.Resolve("/v1/demo")),
                PacJsonContext.Default.ChangeWatermarksResponse,
                TestContext.Current.CancellationToken,
                Guid.Empty));

        Assert.Equal("commandId", ex.ParamName);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task PostJsonAsync_attaches_command_id_and_deserializes()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(HttpStatusCode.OK, """{"items":[{"topic":"t","version":1}]}""");

        using var client = CreateClient(token, api);
        var commandId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var body = await client.PostJsonAsync(
            () => new HttpRequestMessage(HttpMethod.Post, client.Resolve("/v1/demo"))
            {
                Content = new StringContent("""{"name":"x"}""", Encoding.UTF8, "application/json"),
            },
            PacJsonContext.Default.ChangeWatermarksResponse,
            TestContext.Current.CancellationToken,
            commandId);

        Assert.NotNull(body);
        Assert.Single(body.Items);
        var call = Assert.Single(api.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal(commandId.ToString("D"), call.CommandId);
    }

    [Fact]
    public async Task PutJsonAsync_generates_command_id_when_omitted()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(HttpStatusCode.OK, """{"items":[]}""");

        using var client = CreateClient(token, api);
        _ = await client.PutJsonAsync(
            () => new HttpRequestMessage(HttpMethod.Put, client.Resolve("/v1/demo")),
            PacJsonContext.Default.ChangeWatermarksResponse,
            TestContext.Current.CancellationToken);

        var call = Assert.Single(api.Calls);
        Assert.True(Guid.TryParse(call.CommandId, out var id));
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task DeleteAsync_attaches_command_id()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueStatus(HttpStatusCode.NoContent);

        using var client = CreateClient(token, api);
        var commandId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        await client.DeleteAsync(
            () => new HttpRequestMessage(HttpMethod.Delete, client.Resolve("/v1/demo/1")),
            TestContext.Current.CancellationToken,
            commandId);

        Assert.Equal(commandId.ToString("D"), Assert.Single(api.Calls).CommandId);
    }

    [Fact]
    public async Task PostJsonAsync_conflict_throws_pac_api_conflict_with_current_version()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueJson(
            HttpStatusCode.Conflict,
            """{"title":"conflict","status":409,"code":"conflict","traceId":"t1","currentVersion":42}""");

        using var client = CreateClient(token, api);
        var ex = await Assert.ThrowsAsync<PacApiConflictException>(
            () => client.PostJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, client.Resolve("/v1/demo")),
                PacJsonContext.Default.ChangeWatermarksResponse,
                TestContext.Current.CancellationToken));

        Assert.Equal(409, ex.Status);
        Assert.Equal(42, ex.CurrentVersion);
        Assert.True(ex.IsConflict);
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public async Task GetJsonAsync_rejects_oversized_content_length()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.Enqueue(
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json"),
                };
                response.Content.Headers.ContentLength = PacApiClient.MaxResponseBytes + 1;
                return response;
            });

        using var client = CreateClient(token, api);
        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => client.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/demo")),
                PacJsonContext.Default.ChangeWatermarksResponse,
                TestContext.Current.CancellationToken));

        Assert.Equal("response_too_large", ex.Code);
        Assert.Equal(502, ex.Status);
    }

    [Fact]
    public async Task Timeout_without_caller_cancel_wraps_as_timeout_code()
    {
        using var pac = new PacApiClient(
            "http://127.0.0.1:9",
            "key",
            new NullLogger(),
            "X-Api-Key",
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"accessToken":"t","tokenType":"Bearer","expiresIn":3600,"clientId":"c"}""",
                    Encoding.UTF8,
                    "application/json"),
            })),
            new ScriptHandler((_, _) => throw new TaskCanceledException("timed out", new TimeoutException("inner"))),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => pac.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, pac.Resolve("/v1/x")),
                CancellationToken.None));

        Assert.Equal("timeout", ex.Code);
        Assert.Equal(504, ex.Status);
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task PostJsonAsync_on_401_does_not_replay_or_refresh_token()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        api.EnqueueStatus(HttpStatusCode.Unauthorized);
        // 若错误重放，第二次会吃掉这条并变成 200
        api.EnqueueJson(HttpStatusCode.OK, """{"items":[]}""");

        using var client = CreateClient(token, api);
        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => client.PostJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, client.Resolve("/v1/demo")),
                PacJsonContext.Default.ChangeWatermarksResponse,
                TestContext.Current.CancellationToken));

        Assert.Equal(401, ex.Status);
        Assert.Single(token.Calls);
        Assert.Single(api.Calls);
        Assert.Equal("tok-1", api.Calls[0].Bearer);
        Assert.True(Guid.TryParse(api.Calls[0].CommandId, out _));
        Assert.Null(client.CurrentAccessToken);
    }

    [Fact]
    public async Task PostJsonAsync_after_401_next_write_refreshes_token_without_replaying_first()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        token.EnqueueToken("tok-2");
        api.EnqueueStatus(HttpStatusCode.Unauthorized);
        api.EnqueueJson(HttpStatusCode.OK, """{"items":[{"topic":"t","version":1}]}""");

        using var client = CreateClient(token, api);
        var ct = TestContext.Current.CancellationToken;

        var first = await Assert.ThrowsAsync<PacApiException>(
            () => client.PostJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Post, client.Resolve("/v1/demo")),
                PacJsonContext.Default.ChangeWatermarksResponse,
                ct));
        Assert.Equal(401, first.Status);
        Assert.Single(api.Calls);
        Assert.Equal("tok-1", api.Calls[0].Bearer);

        var body = await client.PostJsonAsync(
            () => new HttpRequestMessage(HttpMethod.Post, client.Resolve("/v1/demo")),
            PacJsonContext.Default.ChangeWatermarksResponse,
            ct);

        Assert.NotNull(body);
        Assert.Equal(2, token.Calls.Count);
        Assert.Equal(2, api.Calls.Count);
        Assert.Equal("tok-2", api.Calls[1].Bearer);
        Assert.Equal("tok-2", client.CurrentAccessToken);
    }

    [Fact]
    public async Task GetJsonAsync_on_401_still_refreshes_and_retries()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        token.EnqueueToken("tok-1");
        token.EnqueueToken("tok-2");
        api.EnqueueStatus(HttpStatusCode.Unauthorized);
        api.EnqueueJson(HttpStatusCode.OK, """{"items":[{"topic":"t","version":1}]}""");

        using var client = CreateClient(token, api);
        var body = await client.GetJsonAsync(
            () => new HttpRequestMessage(HttpMethod.Get, client.Resolve("/v1/demo")),
            PacJsonContext.Default.ChangeWatermarksResponse,
            TestContext.Current.CancellationToken);

        Assert.NotNull(body);
        Assert.Equal(2, token.Calls.Count);
        Assert.Equal(2, api.Calls.Count);
        Assert.All(api.Calls, c => Assert.Null(c.CommandId));
    }

    [Fact]
    public async Task Token_response_without_content_length_rejects_oversized_body()
    {
        var oversized = new string('a', (int)PacApiClient.MaxResponseBytes + 8);
        var json = $$"""{"accessToken":"{{oversized}}","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""";

        using var pac = new PacApiClient(
            "http://127.0.0.1:9",
            "key",
            new NullLogger(),
            "X-Api-Key",
            new ScriptHandler((_, _) =>
            {
                var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(json)));
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                // 故意不设 Content-Length，走 LimitedReadStream 计数路径
                content.Headers.ContentLength = null;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => pac.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, pac.Resolve("/v1/x")),
                TestContext.Current.CancellationToken));

        Assert.Equal("response_too_large", ex.Code);
        Assert.Equal(502, ex.Status);
    }

    [Fact]
    public async Task Caller_cancel_rethrows_operation_canceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var pac = new PacApiClient(
            "http://127.0.0.1:9",
            "key",
            new NullLogger(),
            "X-Api-Key",
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))),
            new ScriptHandler((_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pac.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, pac.Resolve("/v1/x")),
                cts.Token));
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

    private sealed record Call(HttpMethod Method, Uri Uri, string? Bearer, string? CommandId);

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

        public void EnqueueStatus(HttpStatusCode status)
            => Enqueue(_ => new HttpResponseMessage(status));

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
            string? bearer = null;
            if (request.Headers.Authorization is { Scheme: "Bearer" } auth)
            {
                bearer = auth.Parameter;
            }

            string? commandId = null;
            if (request.Headers.TryGetValues(PacApiHeaders.CommandId, out var values))
            {
                commandId = values.FirstOrDefault();
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
                    commandId));
                return Task.FromResult(_responses.Dequeue()(request));
            }
        }
    }

    private sealed class ScriptHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;

        public ScriptHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn)
            => _fn = fn;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _fn(request, cancellationToken);
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/pac-api-write-client-test.log";

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
