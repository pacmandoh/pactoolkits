using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Tests;

[Collection(nameof(ApiChangeWatermarkCollection))]
public sealed class ApiChangeWatermarkTests
{
    [Fact]
    public async Task Ready_fetches_watermarks_without_emitting_first_seen_topics()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        api.FallbackJson = """{"items":[{"topic":"inventory","version":1}]}""";
        sse.EnqueueSse("event: ready\ndata: {}\n\n");

        var topics = new List<string>();
        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac);
        watermark.TopicChanged += topics.Add;
        watermark.Start();

        // poll + ready 各一次 GET；不能只靠 poll 带过
        await WaitAsync(
            () => sse.Calls.Count >= 1 && api.WatermarkGets >= 2,
            TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Empty(topics);
    }

    [Fact]
    public async Task Reconnect_ready_fetches_watermarks_again()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        api.FallbackJson = """{"items":[{"topic":"inventory","version":1}]}""";
        sse.EnqueueSse("event: ready\ndata: {}\n\n");

        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac);
        watermark.Start();

        // 先等到首段 ready 补偿完成，再投递下一段，避免与合并窗抢时序
        await WaitAsync(
            () => sse.Calls.Count >= 1 && api.WatermarkGets >= 2,
            TestContext.Current.CancellationToken);
        var baselineGets = api.WatermarkGets;

        sse.EnqueueSse("event: ready\ndata: {}\n\n");

        await WaitAsync(
            () => sse.Calls.Count >= 2 && api.WatermarkGets > baselineGets,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Version_bump_after_ready_emits_topic()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        var version = 1L;
        api.FallbackFactory = _ => WatermarkJson(version);
        sse.EnqueueSse("event: ready\ndata: {}\n\n");

        var topics = new List<string>();
        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac);
        watermark.TopicChanged += t =>
        {
            lock (topics)
            {
                topics.Add(t);
            }
        };
        watermark.Start();

        // 与 Ready 用例相同：须等到 SSE ready 补偿，不能只靠 poll
        await WaitAsync(
            () => sse.Calls.Count >= 1 && api.WatermarkGets >= 2,
            TestContext.Current.CancellationToken);
        await Task.Delay(80, TestContext.Current.CancellationToken);
        lock (topics)
        {
            Assert.Empty(topics);
        }

        version = 2;
        sse.EnqueueSse("event: change\ndata: {\"topic\":\"inventory\"}\n\n");

        await WaitAsync(
            () =>
            {
                lock (topics)
                {
                    return topics.Contains("inventory");
                }
            },
            TestContext.Current.CancellationToken);
    }

    private static PacApiClient CreatePac(ScriptedHandler token, ScriptedHandler api, ScriptedHandler sse)
        => new(
            "http://127.0.0.1:5080",
            "test-key",
            new NullLogger(),
            "X-Api-Key",
            token,
            api,
            sse);

    private static ApiChangeWatermark CreateWatermark(PacApiClient pac)
        => new(
            pac,
            new NullLogger(),
            pollInterval: TimeSpan.FromHours(1),
            reconnectDelay: TimeSpan.FromMilliseconds(40),
            notifyCoalesceWindow: TimeSpan.FromMilliseconds(20));

    private static string TokenJson(string accessToken)
        => $$"""{"accessToken":"{{accessToken}}","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""";

    private static string WatermarkJson(long version)
        => $$"""{"items":[{"topic":"inventory","version":{{version}}}]}""";

    private static async Task WaitAsync(Func<bool> condition, CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + 8_000;
        while (Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (condition())
            {
                return;
            }

            await Task.Delay(20, ct);
        }

        Assert.Fail("timed out waiting for condition");
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly object _gate = new();
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public string? FallbackJson { get; set; }

        public Func<HttpRequestMessage, string>? FallbackFactory { get; set; }

        public List<Uri> Calls { get; } = [];

        public int WatermarkGets { get; private set; }

        public void EnqueueJson(string json)
        {
            lock (_gate)
            {
                _responses.Enqueue(_ => JsonResponse(json));
            }
        }

        public void EnqueueSse(string body)
        {
            lock (_gate)
            {
                _responses.Enqueue(_ =>
                {
                    var content = new SseContent(body);
                    content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
                });
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? new Uri("http://invalid/");
            lock (_gate)
            {
                Calls.Add(uri);
                if (uri.AbsolutePath.Contains("/v1/changes/watermarks", StringComparison.Ordinal))
                {
                    WatermarkGets++;
                }

                if (_responses.Count > 0)
                {
                    return Task.FromResult(_responses.Dequeue()(request));
                }
            }

            if (FallbackFactory is not null)
            {
                return Task.FromResult(JsonResponse(FallbackFactory(request)));
            }

            if (FallbackJson is not null)
            {
                return Task.FromResult(JsonResponse(FallbackJson));
            }

            throw new InvalidOperationException($"unexpected request {request.Method} {uri}");
        }

        private static HttpResponseMessage JsonResponse(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class SseContent(string body) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(body);

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => stream.WriteAsync(_bytes).AsTask();

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context,
            CancellationToken cancellationToken)
            => stream.WriteAsync(_bytes, cancellationToken).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new MemoryStream(_bytes, writable: false));

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => CreateContentReadStreamAsync();

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/pactoolkits-api-change-watermark-test.log";
        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}

[CollectionDefinition(nameof(ApiChangeWatermarkCollection), DisableParallelization = true)]
public sealed class ApiChangeWatermarkCollection;
