using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

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

        // SSE ready 已接通，且至少完成一次水位 GET（poll/ready 可合并成一次）
        await WaitAsync(
            () => sse.Calls.Count >= 1 && api.WatermarkGets >= 1,
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

        // 先等到首段 ready 接通并完成水位刷新，再投递下一段
        await WaitAsync(
            () => sse.Calls.Count >= 1 && api.WatermarkGets >= 1,
            TestContext.Current.CancellationToken);
        await Task.Delay(80, TestContext.Current.CancellationToken);
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

        // 与 Ready 用例相同：须等到 SSE ready 接通并完成水位刷新
        await WaitAsync(
            () => sse.Calls.Count >= 1 && api.WatermarkGets >= 1,
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

    [Fact]
    public async Task TopicChanged_handler_exception_does_not_block_other_topics()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        var version = 1L;
        api.FallbackFactory = _ =>
            $$"""{"items":[{"topic":"inventory","version":{{version}}},{"topic":"trace","version":{{version}}}]}""";
        sse.EnqueueSse("event: ready\ndata: {}\n\n");

        var seen = new List<string>();
        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac);
        watermark.TopicChanged += topic =>
        {
            if (string.Equals(topic, "inventory", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("subscriber boom");
            }

            lock (seen)
            {
                seen.Add(topic);
            }
        };
        watermark.Start();

        await WaitAsync(
            () => sse.Calls.Count >= 1 && api.WatermarkGets >= 1,
            TestContext.Current.CancellationToken);
        await Task.Delay(80, TestContext.Current.CancellationToken);

        version = 2;
        sse.EnqueueSse("event: change\ndata: {\"topic\":\"inventory\"}\n\n");

        await WaitAsync(
            () =>
            {
                lock (seen)
                {
                    return seen.Contains("trace");
                }
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Failed_watermark_get_keeps_change_emit_for_retry()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        // 第一次水位 GET 失败；若错误地消费了强唤醒，重试会静默登记且不通知
        api.EnqueueFault(new IOException("watermark cut"));
        api.FallbackJson = """{"items":[{"topic":"inventory","version":1}]}""";
        sse.EnqueueSse("event: change\ndata: {\"topic\":\"inventory\"}\n\n");
        sse.EnqueueHangingSse();

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

        await WaitAsync(
            () =>
            {
                lock (topics)
                {
                    return topics.Contains("inventory");
                }
            },
            TestContext.Current.CancellationToken);

        Assert.True(api.WatermarkGets >= 2, $"expected retry after failed GET, got {api.WatermarkGets}");
    }

    [Fact]
    public async Task Burst_change_pulses_coalesce_watermark_gets_without_losing_notify()
    {
        const int pulseCount = 80;
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        var version = 1L;
        api.FallbackFactory = _ =>
            $$"""{"items":[{"topic":"inventory","version":{{version}}},{"topic":"trace","version":{{version}}}]}""";
        sse.EnqueueSse("event: ready\ndata: {}\n\n");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac);
        watermark.TopicChanged += topic =>
        {
            lock (seen)
            {
                seen.Add(topic);
            }
        };
        watermark.Start();

        await WaitAsync(
            () => sse.Calls.Count >= 1 && api.WatermarkGets >= 1,
            TestContext.Current.CancellationToken);
        await Task.Delay(80, TestContext.Current.CancellationToken);
        var baselineGets = api.WatermarkGets;

        version = 2;
        sse.EnqueueSse(BuildChangeBurst(pulseCount));
        // 洪峰结束后挂住 SSE，避免重连抢时序
        sse.EnqueueHangingSse();

        await WaitAsync(
            () =>
            {
                lock (seen)
                {
                    return seen.Contains("inventory") && seen.Contains("trace");
                }
            },
            TestContext.Current.CancellationToken);

        // 再等一窗合并结束，确认没有按脉冲数重复拉 GET
        await Task.Delay(120, TestContext.Current.CancellationToken);
        var burstGets = api.WatermarkGets - baselineGets;
        Assert.True(
            burstGets > 0 && burstGets <= 5,
            $"expected coalesced watermark GETs in 1..5 after {pulseCount} pulses, got {burstGets}");
        Assert.True(burstGets < pulseCount / 8, $"watermark GETs {burstGets} not coalesced vs {pulseCount} pulses");
    }

    [Fact]
    public void ResolveSseBackoff_prefers_RetryAfter()
    {
        using var pac = CreatePac(new ScriptedHandler(), new ScriptedHandler(), new ScriptedHandler());
        using var watermark = CreateWatermark(pac);

        Assert.Equal(TimeSpan.FromSeconds(12), watermark.ResolveSseBackoff(TimeSpan.FromSeconds(12)));
        Assert.Equal(TimeSpan.FromSeconds(60), watermark.ResolveSseBackoff(TimeSpan.FromSeconds(120)));
        // 低于 reconnectDelay（测试里 40ms）时抬到 base
        Assert.Equal(TimeSpan.FromMilliseconds(40), watermark.ResolveSseBackoff(TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public void ResolveSseBackoff_grows_without_header()
    {
        using var pac = CreatePac(new ScriptedHandler(), new ScriptedHandler(), new ScriptedHandler());
        using var watermark = CreateWatermark(pac);

        var first = watermark.ResolveSseBackoff(retryAfter: null);
        var second = watermark.ResolveSseBackoff(retryAfter: null);

        Assert.InRange(first.TotalMilliseconds, 32, 48); // base 40ms ±20%
        Assert.True(second > first);
    }

    [Fact]
    public async Task Ready_then_eof_still_grows_sse_backoff()
    {
        var time = new ControllableTime();
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        api.FallbackJson = """{"items":[]}""";
        // 连续 ready 后 EOF；若 ready 误清零，streak 会卡在 1
        sse.EnqueueSse("event: ready\ndata: {}\n\n");
        sse.EnqueueSse("event: ready\ndata: {}\n\n");
        sse.EnqueueSse("event: ready\ndata: {}\n\n");
        sse.EnqueueHangingSse();

        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac, time);
        watermark.Start();

        await WaitAsync(() => watermark.TestSseFailStreak >= 1, TestContext.Current.CancellationToken);
        Assert.Equal(1, watermark.TestSseFailStreak);

        time.Advance(TimeSpan.FromMilliseconds(80));
        await WaitAsync(() => watermark.TestSseFailStreak >= 2, TestContext.Current.CancellationToken);
        Assert.Equal(2, watermark.TestSseFailStreak);

        time.Advance(TimeSpan.FromMilliseconds(120));
        await WaitAsync(() => watermark.TestSseFailStreak >= 3, TestContext.Current.CancellationToken);
        Assert.True(watermark.TestSseFailStreak >= 3);
    }

    [Fact]
    public async Task Heartbeat_resets_sse_backoff_after_failures()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        api.FallbackJson = """{"items":[]}""";
        sse.EnqueuePrefixHangSse("event: ready\ndata: {}\n\nevent: heartbeat\ndata: {\"utc\":\"2026-01-01T00:00:00Z\"}\n\n");

        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac);
        _ = watermark.ResolveSseBackoff(null);
        _ = watermark.ResolveSseBackoff(null);
        Assert.True(watermark.TestSseFailStreak >= 2);

        watermark.Start();
        await WaitAsync(() => watermark.TestSseFailStreak == 0, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Concurrent_Start_starts_each_loop_once()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        api.FallbackJson = """{"items":[]}""";
        sse.EnqueueSse("event: ready\ndata: {}\n\n");

        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(watermark.Start)));
        await Task.Delay(80, TestContext.Current.CancellationToken);

        Assert.Equal(1, watermark.TestDispatchLoopStarts);
        Assert.Equal(1, watermark.TestPollLoopStarts);
        Assert.Equal(1, watermark.TestStreamLoopStarts);
    }

    [Fact]
    public async Task Reset_aborts_hanging_sse_and_bootstrap_emits_topics()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        token.EnqueueJson(TokenJson("tok-2"));
        api.FallbackJson = """{"items":[{"topic":"inventory","version":1}]}""";
        sse.EnqueueHangingSse();
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

        await WaitAsync(() => sse.Calls.Count >= 1, TestContext.Current.CancellationToken);
        await Task.Delay(60, TestContext.Current.CancellationToken);

        watermark.Reset();

        await WaitAsync(
            () =>
            {
                lock (topics)
                {
                    return sse.Calls.Count >= 2 && topics.Contains("inventory");
                }
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, watermark.TestStreamLoopStarts);
    }

    [Fact]
    public async Task Reset_after_silent_ready_emits_first_seen_topics()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        api.FallbackJson = """{"items":[{"topic":"inventory","version":1}]}""";
        sse.EnqueueSse("event: ready\ndata: {}\n\n");
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

        await WaitAsync(
            () => sse.Calls.Count >= 1 && api.WatermarkGets >= 1,
            TestContext.Current.CancellationToken);
        await Task.Delay(80, TestContext.Current.CancellationToken);
        lock (topics)
        {
            Assert.Empty(topics);
        }

        watermark.Reset();

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

    [Fact]
    public async Task Reset_discards_in_flight_watermark_from_prior_epoch()
    {
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        token.EnqueueJson(TokenJson("tok-2"));
        var stale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.EnqueueDelayedJson(WatermarkJson(100), stale.Task);
        var version = 1L;
        api.FallbackFactory = _ => WatermarkJson(version);
        sse.EnqueueHangingSse();
        sse.EnqueueHangingSse();

        var topics = new List<string>();
        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac, pollInterval: TimeSpan.FromMilliseconds(50));
        watermark.TopicChanged += t =>
        {
            lock (topics)
            {
                topics.Add(t);
            }
        };
        watermark.Start();

        await WaitAsync(() => api.WatermarkGets >= 1, TestContext.Current.CancellationToken);

        pac.Apply(new PacApiOptions
        {
            BaseUrl = "http://127.0.0.1:5081",
            ApiKey = "key-2",
        });
        watermark.Reset();
        stale.TrySetResult();

        await WaitAsync(() => api.WatermarkGets >= 2, TestContext.Current.CancellationToken);
        await Task.Delay(80, TestContext.Current.CancellationToken);
        lock (topics)
        {
            topics.Clear();
        }

        version = 2;

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

    [Fact]
    public async Task Reset_aborts_sse_backoff_without_waiting()
    {
        var time = new ControllableTime();
        var token = new ScriptedHandler();
        var api = new ScriptedHandler();
        var sse = new ScriptedHandler();
        token.EnqueueJson(TokenJson("tok-1"));
        token.EnqueueJson(TokenJson("tok-2"));
        api.FallbackJson = """{"items":[]}""";
        sse.EnqueueStatus(HttpStatusCode.ServiceUnavailable);
        sse.EnqueueHangingSse();

        using var pac = CreatePac(token, api, sse);
        using var watermark = CreateWatermark(pac, time);
        watermark.Start();

        await WaitAsync(() => sse.Calls.Count >= 1, TestContext.Current.CancellationToken);
        await Task.Delay(40, TestContext.Current.CancellationToken);
        Assert.Single(sse.Calls);

        watermark.Reset();

        await WaitAsync(() => sse.Calls.Count >= 2, TestContext.Current.CancellationToken);
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

    private static ApiChangeWatermark CreateWatermark(
        PacApiClient pac,
        TimeProvider? time = null,
        TimeSpan? pollInterval = null)
        => new(
            pac,
            new NullLogger(),
            pollInterval: pollInterval ?? TimeSpan.FromHours(1),
            reconnectDelay: TimeSpan.FromMilliseconds(40),
            notifyCoalesceWindow: TimeSpan.FromMilliseconds(20),
            timeProvider: time);

    private static string TokenJson(string accessToken)
        => $$"""{"accessToken":"{{accessToken}}","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""";

    private static string WatermarkJson(long version)
        => $$"""{"items":[{"topic":"inventory","version":{{version}}}]}""";

    private static string BuildChangeBurst(int pulseCount)
    {
        var sb = new StringBuilder(pulseCount * 48);
        for (var i = 0; i < pulseCount; i++)
        {
            // 交替 topic：合并后两个 version bump 都要通知到
            var topic = (i & 1) == 0 ? "inventory" : "trace";
            sb.Append("event: change\ndata: {\"topic\":\"").Append(topic).Append("\"}\n\n");
        }

        return sb.ToString();
    }

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
        private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

        public string? FallbackJson { get; set; }

        public Func<HttpRequestMessage, string>? FallbackFactory { get; set; }

        public List<Uri> Calls { get; } = [];

        public int WatermarkGets { get; private set; }

        public void EnqueueJson(string json)
        {
            lock (_gate)
            {
                _responses.Enqueue((_, _) => Task.FromResult(JsonResponse(json)));
            }
        }

        public void EnqueueDelayedJson(string json, Task release)
        {
            lock (_gate)
            {
                _responses.Enqueue(async (_, _) =>
                {
                    await release.ConfigureAwait(false);
                    return JsonResponse(json);
                });
            }
        }

        public void EnqueueFault(Exception ex)
        {
            lock (_gate)
            {
                _responses.Enqueue((_, _) => throw ex);
            }
        }

        public void EnqueueStatus(HttpStatusCode status)
        {
            lock (_gate)
            {
                _responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(status)));
            }
        }

        public void EnqueueSse(string body)
        {
            lock (_gate)
            {
                _responses.Enqueue((_, _) =>
                {
                    var content = new SseContent(body);
                    content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
                });
            }
        }

        public void EnqueueHangingSse()
        {
            lock (_gate)
            {
                _responses.Enqueue((_, _) =>
                {
                    var content = new HangingSseContent();
                    content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
                });
            }
        }

        public void EnqueuePrefixHangSse(string prefix)
        {
            lock (_gate)
            {
                _responses.Enqueue((_, _) =>
                {
                    var content = new PrefixHangSseContent(prefix);
                    content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
                });
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? new Uri("http://invalid/");
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? next = null;
            lock (_gate)
            {
                Calls.Add(uri);
                if (uri.AbsolutePath.Contains("/v1/changes/watermarks", StringComparison.Ordinal))
                {
                    WatermarkGets++;
                }

                if (_responses.Count > 0)
                {
                    next = _responses.Dequeue();
                }
            }

            if (next is not null)
            {
                return next(request, cancellationToken);
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

    private sealed class HangingSseContent : HttpContent
    {
        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new PrefixHangStream([]));

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => CreateContentReadStreamAsync();

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context,
            CancellationToken cancellationToken)
            => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class PrefixHangSseContent(string prefix) : HttpContent
    {
        private readonly byte[] _prefix = Encoding.UTF8.GetBytes(prefix);

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new PrefixHangStream(_prefix));

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => CreateContentReadStreamAsync();

        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context,
            CancellationToken cancellationToken)
        {
            await stream.WriteAsync(_prefix, cancellationToken).ConfigureAwait(false);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class PrefixHangStream(byte[] prefix) : Stream
    {
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_offset < prefix.Length)
            {
                var n = Math.Min(buffer.Length, prefix.Length - _offset);
                prefix.AsSpan(_offset, n).CopyTo(buffer.Span);
                _offset += n;
                return n;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
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
