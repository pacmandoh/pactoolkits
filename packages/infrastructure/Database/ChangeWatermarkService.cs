using System.Threading.Channels;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Diagnostics;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 业务变更水位监听与分发
///
/// 结合 PostgreSQL 通知与 <c>app_change_watermark</c> 轮询，按主题发布可靠的变更通知
/// 不负责具体页面刷新逻辑
/// </summary>
public sealed class ChangeWatermarkService : IChangeWatermarkService
{
    private const string NotifyChannel = "pactoolkits_change";

    private readonly IChangeWatermarkRepo _watermarks;
    private readonly IDbConfigService _dbConfig;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, long> _versions = new(StringComparer.OrdinalIgnoreCase);
    // poll 静默登记的 topic；NOTIFY 到达时即使 version 未涨也补发一次
    private readonly HashSet<string> _silentBootstrap = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(20);
    private readonly TimeSpan _listenRetryDelay = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _notifyCoalesceWindow = TimeSpan.FromMilliseconds(120);

    // poll / LISTEN 都只唤醒；单一 Dispatch 串行刷 watermark，合并窗内 OR emitOnBootstrap
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
    private Task? _listenLoop;
    private Task? _pollLoop;
    private Task? _dispatchLoop;
    private int _dispatchLoopStarts;
    private int _pollLoopStarts;
    private int _listenLoopStarts;

    public event Action<string>? TopicChanged;

    public ChangeWatermarkService(
        IChangeWatermarkRepo watermarks,
        IDbConfigService dbConfig,
        IAppLogger logger,
        TimeProvider timeProvider)
    {
        _watermarks = watermarks ?? throw new ArgumentNullException(nameof(watermarks));
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    public void Start()
    {
        lock (_startGate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _dispatchLoop = Task.Run(() => DispatchLoopAsync(_cts.Token));
            _pollLoop = Task.Run(() => PollLoopAsync(_cts.Token));
            _listenLoop = Task.Run(() => ListenLoopAsync(_cts.Token));
        }
    }

    internal int TestDispatchLoopStarts => Volatile.Read(ref _dispatchLoopStarts);

    internal int TestPollLoopStarts => Volatile.Read(ref _pollLoopStarts);

    internal int TestListenLoopStarts => Volatile.Read(ref _listenLoopStarts);

    public void Dispose()
    {
        try { _cts.Cancel(); }
        catch (System.Exception ex)
        {
            _logger.Warn("ChangeWatermark", "watermark.dispose.cancel_fail", "Failed to cancel watermark service", ex);
        }

        try { _wake.Writer.TryComplete(); }
        catch (System.Exception ex)
        {
            _logger.Warn("ChangeWatermark", "watermark.dispose.channel_close_fail", "Failed to close watermark channel", ex);
        }

        _cts.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _pollLoopStarts);
        while (!ct.IsCancellationRequested)
        {
            // 轮询只补 version；首次登记不刷页，NOTIFY 才首刷
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

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref _listenLoopStarts);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(BuildListenConnectionString());
                await conn.OpenAsync(ct).ConfigureAwait(false);

                conn.Notification += (_, _) =>
                {
                    EnqueuePulse(emitOnBootstrap: true);
                };

                await using (var listen = new NpgsqlCommand($"LISTEN {NotifyChannel};", conn))
                {
                    await listen.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                while (!ct.IsCancellationRequested)
                {
                    await conn.WaitAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(_listenRetryDelay, _time, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
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

                // 短窗内合并突发唤醒，避免一次水位刷新打成多次
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
            catch (System.Exception ex) when (ex is not OperationCanceledException)
            {
                // 失败时退回强标记并再唤醒，避免 NOTIFY 语义被一次失败吞掉
                lock (_pulseGate)
                {
                    _pendingEmitOnBootstrap |= emitOnBootstrap;
                }

                _logger.Warn("ChangeWatermark", "watermark.dispatch.fail", "Watermark dispatch failed", ex);
                try
                {
                    await Task.Delay(_listenRetryDelay, _time, ct).ConfigureAwait(false);
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
        using var activity = PacActivities.Desktop.StartActivity("watermark.refresh");
        var rows = await _watermarks.ListAsync(ct).ConfigureAwait(false);

        foreach (var row in rows)
        {
            var topic = row.Topic;
            var version = row.Version;
            var shouldEmit = false;

            lock (_gate)
            {
                if (_versions.TryGetValue(topic, out var prev))
                {
                    if (version > prev)
                    {
                        _versions[topic] = version;
                        _silentBootstrap.Remove(topic);
                        shouldEmit = true;
                    }
                    else if (emitOnBootstrap && _silentBootstrap.Remove(topic))
                    {
                        shouldEmit = true;
                    }
                }
                else
                {
                    _versions[topic] = version;
                    // 首次见到：NOTIFY/change 才刷页；poll 静默登记，留给后续 change 补发
                    if (emitOnBootstrap)
                    {
                        shouldEmit = true;
                    }
                    else
                    {
                        _silentBootstrap.Add(topic);
                    }
                }
            }

            if (shouldEmit)
            {
                RaiseTopicChanged(topic);
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

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<string>)subscriber).Invoke(topic);
            }
            catch (System.Exception ex)
            {
                _logger.Warn(
                    "ChangeWatermark",
                    "topic.notify.fail",
                    $"TopicChanged handler failed for {topic}",
                    ex);
            }
        }
    }

    private string BuildListenConnectionString()
    {
        var opt = _dbConfig.Current;
        var csb = new NpgsqlConnectionStringBuilder(PgConnectionFactory.BuildConnectionString(
            opt,
            timeoutSeconds: Math.Max(3, opt.ConnectTimeoutSeconds)))
        {
            KeepAlive = Math.Max(5, opt.KeepAliveSeconds),
            // PostgreSQL 通知会话必须独占连接，否则通知可能由其他池化连接接收
            Pooling = false
        };
        return csb.ConnectionString;
    }
}
