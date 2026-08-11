using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Diagnostics;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>
/// 经 API 的变更水位：SSE 只负责唤醒；version 以 GET watermarks 为准
///
/// 不直连 Pg NOTIFY；SSE 断了只重连流，不要把整站判成断开
/// ready 与 change 都补查 watermark；第一次见到的 topic 只有 change 才刷页
/// Desktop DI 唯一的 <see cref="IChangeWatermarkService"/>
/// </summary>
public sealed class ApiChangeWatermark : IChangeWatermarkService
{
    private readonly PacApiClient _api;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, long> _versions = new(StringComparer.OrdinalIgnoreCase);
    // poll 与 ready 静默登记的 topic；随后 change 即使 version 未涨也要补发一次
    private readonly HashSet<string> _silentBootstrap = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _reconnectDelay;
    private readonly TimeSpan _notifyCoalesceWindow;
    private static readonly TimeSpan SseMaxBackoff = TimeSpan.FromSeconds(60);
    private int _sseFailStreak;

    // 容量 1 唤醒；锁内合并 emitOnBootstrap（对齐 API LISTEN 的 pending）
    private readonly object _pulseGate = new();
    private bool _pendingEmitOnBootstrap;
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

    private readonly object _startGate = new();
    private bool _started;
    private Task? _streamLoop;
    private Task? _pollLoop;
    private Task? _dispatchLoop;
    private int _dispatchLoopStarts;
    private int _pollLoopStarts;
    private int _streamLoopStarts;

    public event Action<string>? TopicChanged;

    public ApiChangeWatermark(PacApiClient api, IAppLogger logger, TimeProvider timeProvider)
        : this(
            api,
            logger,
            timeProvider,
            pollInterval: TimeSpan.FromSeconds(20),
            reconnectDelay: TimeSpan.FromSeconds(2),
            notifyCoalesceWindow: TimeSpan.FromMilliseconds(120))
    {
    }

    internal ApiChangeWatermark(
        PacApiClient api,
        IAppLogger logger,
        TimeSpan pollInterval,
        TimeSpan reconnectDelay,
        TimeSpan notifyCoalesceWindow,
        TimeProvider? timeProvider = null)
        : this(api, logger, timeProvider ?? TimeProvider.System, pollInterval, reconnectDelay, notifyCoalesceWindow)
    {
    }

    private ApiChangeWatermark(
        PacApiClient api,
        IAppLogger logger,
        TimeProvider timeProvider,
        TimeSpan pollInterval,
        TimeSpan reconnectDelay,
        TimeSpan notifyCoalesceWindow)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
        _pollInterval = pollInterval;
        _reconnectDelay = reconnectDelay;
        _notifyCoalesceWindow = notifyCoalesceWindow;
    }

    public void Start()
    {
        if (!_api.IsConfigured)
        {
            _logger.Error(
                "ChangeStream",
                "start.unconfigured",
                "Change API baseUrl/apiKey required; change stream not started");
            return;
        }

        lock (_startGate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _dispatchLoop = Task.Run(() => DispatchLoopAsync(_cts.Token));
            _pollLoop = Task.Run(() => PollLoopAsync(_cts.Token));
            _streamLoop = Task.Run(() => StreamLoopAsync(_cts.Token));
        }
    }

    internal int TestDispatchLoopStarts => Volatile.Read(ref _dispatchLoopStarts);

    internal int TestPollLoopStarts => Volatile.Read(ref _pollLoopStarts);

    internal int TestStreamLoopStarts => Volatile.Read(ref _streamLoopStarts);

    public void Dispose()
    {
        try { _cts.Cancel(); }
        catch (Exception ex)
        {
            _logger.Warn("ChangeStream", "dispose.cancel_fail", "Failed to cancel change stream", ex);
        }

        try { _wake.Writer.TryComplete(); }
        catch (Exception ex)
        {
            _logger.Warn("ChangeStream", "dispose.channel_fail", "Failed to close change stream channel", ex);
        }

        _cts.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _pollLoopStarts);
        while (!ct.IsCancellationRequested)
        {
            // 与 ready 一样只唤醒；Dispatch 串行刷表，避免与 change 竞态吞通知
            EnqueuePulse(emitOnBootstrap: false);

            try
            {
                await Task.Delay(_pollInterval, _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task StreamLoopAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _streamLoopStarts);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await _api.SendSseAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/changes/stream")),
                    ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var problem = await PacApiClient.CreateExceptionAsync(
                            response,
                            _time,
                            _logger,
                            ct)
                        .ConfigureAwait(false);
                    _logger.Warn(
                        "ChangeStream",
                        "sse.http_fail",
                        $"SSE HTTP {problem.Status}");
                    await DelaySseReconnectAsync(problem.RetryAfter, ct).ConfigureAwait(false);
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                _logger.Info("ChangeStream", "sse.up", "Change SSE connected");

                string? eventName = null;
                while (!ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }

                    if (line.Length == 0)
                    {
                        eventName = null;
                        continue;
                    }

                    if (line.StartsWith("event:", StringComparison.Ordinal))
                    {
                        eventName = line["event:".Length..].Trim();
                        continue;
                    }

                    if (!line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.Equals(eventName, "change", StringComparison.OrdinalIgnoreCase))
                    {
                        EnqueuePulse(emitOnBootstrap: true);
                    }
                    else if (string.Equals(eventName, "ready", StringComparison.OrdinalIgnoreCase))
                    {
                        // ready 服务端建连即发，不能证明稳定；第一次见到的 topic 不刷页
                        EnqueuePulse(emitOnBootstrap: false);
                    }
                    else if (string.Equals(eventName, "heartbeat", StringComparison.OrdinalIgnoreCase))
                    {
                        // 首个 heartbeat≈流已存活一轮间隔，此时再清零退避
                        ResetSseBackoff();
                    }
                }

                // 流正常结束也算一次失败重连，沿用指数退避
                await DelaySseReconnectAsync(retryAfter: null, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn("ChangeStream", "sse.retry", "SSE disconnected; reconnecting", ex);
                try
                {
                    await DelaySseReconnectAsync(retryAfter: null, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void ResetSseBackoff()
        => Interlocked.Exchange(ref _sseFailStreak, 0);

    internal int TestSseFailStreak => Volatile.Read(ref _sseFailStreak);

    private async Task DelaySseReconnectAsync(TimeSpan? retryAfter, CancellationToken ct)
    {
        var delay = ResolveSseBackoff(retryAfter);
        await Task.Delay(delay, _time, ct).ConfigureAwait(false);
    }

    /// <summary>有 Retry-After 则夹紧使用；否则 base×2^n 加抖动，封顶 60s</summary>
    internal TimeSpan ResolveSseBackoff(TimeSpan? retryAfter)
    {
        if (retryAfter is { } after && after > TimeSpan.Zero)
        {
            Interlocked.Increment(ref _sseFailStreak);
            return ClampSseBackoff(after);
        }

        var streak = Math.Min(Interlocked.Increment(ref _sseFailStreak) - 1, 5);
        var exp = _reconnectDelay;
        for (var i = 0; i < streak; i++)
        {
            if (exp >= SseMaxBackoff)
            {
                exp = SseMaxBackoff;
                break;
            }

            exp = TimeSpan.FromTicks(Math.Min(exp.Ticks * 2, SseMaxBackoff.Ticks));
        }

        // ±20% 抖动，避免多客户端齐步重连
        var jitterSpan = TimeSpan.FromTicks((long)(exp.Ticks * 0.2));
        if (jitterSpan > TimeSpan.Zero)
        {
            var offset = Random.Shared.NextInt64(-jitterSpan.Ticks, jitterSpan.Ticks + 1);
            exp = TimeSpan.FromTicks(Math.Clamp(exp.Ticks + offset, _reconnectDelay.Ticks, SseMaxBackoff.Ticks));
        }

        return ClampSseBackoff(exp);
    }

    private TimeSpan ClampSseBackoff(TimeSpan value)
    {
        if (value < _reconnectDelay)
        {
            return _reconnectDelay;
        }

        return value > SseMaxBackoff ? SseMaxBackoff : value;
    }

    private void EnqueuePulse(bool emitOnBootstrap)
    {
        lock (_pulseGate)
        {
            _pendingEmitOnBootstrap |= emitOnBootstrap;
        }

        _wake.Writer.TryWrite(true);
    }

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _dispatchLoopStarts);
        while (!ct.IsCancellationRequested)
        {
            bool emitOnBootstrap;
            try
            {
                _ = await _wake.Reader.ReadAsync(ct).ConfigureAwait(false);
                await Task.Delay(_notifyCoalesceWindow, _time, ct).ConfigureAwait(false);
                while (_wake.Reader.TryRead(out _))
                {
                }

                lock (_pulseGate)
                {
                    emitOnBootstrap = _pendingEmitOnBootstrap;
                    _pendingEmitOnBootstrap = false;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RefreshFromWatermarkAsync(emitOnBootstrap, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 失败时退回强标记并再唤醒，避免 change 语义被一次失败吞掉
                lock (_pulseGate)
                {
                    _pendingEmitOnBootstrap |= emitOnBootstrap;
                }

                _logger.Warn("ChangeStream", "watermark.dispatch.fail", "Watermark dispatch failed", ex);
                try
                {
                    await Task.Delay(_reconnectDelay, _time, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                EnqueuePulse(emitOnBootstrap: false);
            }
        }
    }

    private async Task RefreshFromWatermarkAsync(bool emitOnBootstrap, CancellationToken ct)
    {
        using var activity = PacActivities.Desktop.StartActivity("pacapi.watermark.refresh");
        var rows = await FetchWatermarksAsync(ct).ConfigureAwait(false);
        foreach (var row in rows)
        {
            var shouldEmit = false;
            lock (_gate)
            {
                if (_versions.TryGetValue(row.Topic, out var prev))
                {
                    if (row.Version > prev)
                    {
                        _versions[row.Topic] = row.Version;
                        _silentBootstrap.Remove(row.Topic);
                        shouldEmit = true;
                    }
                    else if (emitOnBootstrap && _silentBootstrap.Remove(row.Topic))
                    {
                        shouldEmit = true;
                    }
                }
                else
                {
                    _versions[row.Topic] = row.Version;
                    if (emitOnBootstrap)
                    {
                        shouldEmit = true;
                    }
                    else
                    {
                        _silentBootstrap.Add(row.Topic);
                    }
                }
            }

            if (shouldEmit)
            {
                RaiseTopicChanged(row.Topic);
            }
        }
    }

    private void RaiseTopicChanged(string topic)
    {
        var handler = TopicChanged;
        if (handler is null)
        {
            return;
        }

        // 单页订阅抛错不得挡住其余 topic 与订阅者
        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<string>)subscriber).Invoke(topic);
            }
            catch (Exception ex)
            {
                _logger.Warn(
                    "ChangeStream",
                    "topic.notify.fail",
                    $"TopicChanged handler failed for {topic}",
                    ex);
            }
        }
    }

    private async Task<IReadOnlyList<ChangeWatermarkItem>> FetchWatermarksAsync(CancellationToken ct)
    {
        var body = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/changes/watermarks")),
                PacJsonContext.Default.ChangeWatermarksResponse,
                ct)
            .ConfigureAwait(false);
        return body?.Items ?? Array.Empty<ChangeWatermarkItem>();
    }
}
