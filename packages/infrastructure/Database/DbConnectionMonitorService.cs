using System.Threading.Channels;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Diagnostics;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Threading;


namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 数据库连接存活监控与探测
///
/// 管理后台连接探测、断线与恢复事件，并提供按需探测能力
/// 不修改连接配置；连接串来自 <c>IDbConfigService</c>
/// </summary>
public sealed class DbConnectionMonitorService : IDbConnectionMonitorService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _cts = new();

    private readonly Channel<ProbeRequest> _signals = Channel.CreateUnbounded<ProbeRequest>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly record struct ProbeRequest(DbProbeKind Kind, TaskCompletionSource<DbProbeReport>? Tcs);

    private readonly object _startGate = new();
    private Task? _loop;

    // 无结果的唤醒合并用 writer 侧标记；SingleReader 通道上不要 TryPeek
    private int _reconnectQueued;

    private enum DbConnState
    {
        Unknown,
        Connected,
        Disconnected
    }

    private volatile DbConnState _state = DbConnState.Unknown;

    public bool IsConnected => _state == DbConnState.Connected;
    private bool _disconnectedNotified;
    private int _retryScheduled;
    private string _lastProbeFailReason = string.Empty;
    private DateTimeOffset _lastProbeFailAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan ProbeFailLogThrottle = TimeSpan.FromSeconds(30);

    public event Action? Disconnected;
    public event Action? Reconnected;
    public event Action<string>? ConnectionFailed;

    public DbConnectionMonitorService(IDbConfigService dbConfig, IAppLogger logger, TimeProvider timeProvider)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    public void Start()
    {
        StartMonitorLoop();
        EnqueueSignal();
    }

    public void Signal()
    {
        StartMonitorLoop();
        EnqueueSignal();
    }

    private void StartMonitorLoop()
    {
        if (_loop is not null)
        {
            return;
        }

        // 不能 CompareExchange Task：失败方任务也已在跑，会违反 SingleReader
        lock (_startGate)
        {
            _loop ??= Task.Run(() => RunAsync(_cts.Token));
        }
    }

    private void EnqueueSignal()
    {
        // 已有同类唤醒在队列或处理中则不再写入
        if (Interlocked.CompareExchange(ref _reconnectQueued, 1, 0) != 0)
        {
            return;
        }

        if (!_signals.Writer.TryWrite(new ProbeRequest(DbProbeKind.Reconnect, null)))
        {
            Interlocked.Exchange(ref _reconnectQueued, 0);
        }
    }

    public async Task<DbProbeReport> ProbeAsync(DbProbeKind kind, CancellationToken ct)
    {
        Start();

        var tcs = new TaskCompletionSource<DbProbeReport>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_signals.Writer.TryWrite(new ProbeRequest(kind, tcs)))
        {
            return new DbProbeReport(kind, Success: false, Reason: "无法提交探测请求：监控服务不可用");
        }

        try
        {
            return await tcs.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (_cts.IsCancellationRequested)
            {
                return new DbProbeReport(kind, Success: false, Reason: "探测已中止：监控服务已停止");
            }

            if (ct.IsCancellationRequested)
            {
                return new DbProbeReport(kind, Success: false, Reason: "探测超时：监控未在限定时间内完成");
            }

            return new DbProbeReport(kind, Success: false, Reason: "探测已取消");
        }
    }

    private void ScheduleRetry(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        if (Interlocked.Exchange(ref _retryScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, _time, ct).ConfigureAwait(false);
                if (!ct.IsCancellationRequested)
                {
                    Signal();
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _retryScheduled, 0);
            }
        }, ct);
    }

    public void Dispose()
    {
        try { _cts.Cancel(); }
        catch (System.Exception ex)
        {
            _logger.Warn("DbConnectionMonitor", "monitor.dispose.cancel_fail", "Failed to cancel DB monitor", ex);
        }

        try { _signals.Writer.TryComplete(); }
        catch (System.Exception ex)
        {
            _logger.Warn("DbConnectionMonitor", "monitor.dispose.channel_close_fail", "Failed to close monitor channel", ex);
        }

        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        ProbeRequest? pending = null;
        while (!ct.IsCancellationRequested)
        {
            ProbeRequest req;
            try
            {
                if (pending is { } p)
                {
                    req = p;
                    pending = null;
                }
                else
                {
                    req = await _signals.Reader.ReadAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // 读出后清标记，探测过程中新的 Signal 还能再排队
            if (req.Tcs is null)
            {
                Interlocked.Exchange(ref _reconnectQueued, 0);
            }

            NpgsqlConnection? conn = null;
            using var activity = PacActivities.Desktop.StartActivity("db.probe");
            try
            {
                var opt = _dbConfig.Current;

                conn = OpenForTests is { } openHook
                    ? await openHook(opt, ct).ConfigureAwait(false)
                    : await PgConnectionFactory.OpenAsync(opt, ct).ConfigureAwait(false);

                // 先 SELECT 1，成功后再标 Connected；避免 Open 成功但探测失败仍显示已连接
                await EnsureScalarAsync(conn, opt, ct).ConfigureAwait(false);

                Interlocked.Exchange(ref _retryScheduled, 0);
                _lastProbeFailReason = string.Empty;
                _lastProbeFailAt = DateTimeOffset.MinValue;

                var prev = _state;
                _state = DbConnState.Connected;
                _disconnectedNotified = false;

                if (prev != DbConnState.Connected)
                {
                    Raise(Reconnected, "monitor.reconnected.notify_fail", "Reconnected handler failed");
                }

                req.Tcs?.TrySetResult(new DbProbeReport(req.Kind, Success: true, Reason: null));

                var dropped = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                StartPingLoop(conn, opt, dropped, _time, ct);


                void OnStateChange(object? _, System.Data.StateChangeEventArgs e)
                {
                    if (e.CurrentState == System.Data.ConnectionState.Broken ||
                        e.CurrentState == System.Data.ConnectionState.Closed)
                    {
                        dropped.TrySetResult(null);
                    }
                }

                conn.StateChange += OnStateChange;
                try
                {
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                    var nextSignalTask = _signals.Reader.ReadAsync(waitCts.Token).AsTask();
                    var completed = await Task.WhenAny(dropped.Task, nextSignalTask).ConfigureAwait(false);

                    // 取消待处理的 ReadAsync，确保下一次 Signal 不会被旧读取请求消费
                    try { waitCts.Cancel(); }
                    catch (System.Exception ex)
                    {
                        _logger.Warn("DbConnectionMonitor", "monitor.wait_cancel.fail", "Failed to cancel wait CTS", ex);
                    }

                    if (completed == dropped.Task)
                    {
                        try { await nextSignalTask.ConfigureAwait(false); }
                        catch (System.Exception ex)
                        {
                            _logger.Warn("DbConnectionMonitor", "monitor.next_signal.await_fail", "Failed awaiting pending signal", ex);
                        }

                        var prevState = _state;
                        _state = DbConnState.Disconnected;
                        _disconnectedNotified = true;

                        if (prevState != DbConnState.Disconnected)
                        {
                            Raise(Disconnected, "monitor.disconnected.notify_fail", "Disconnected handler failed");
                            Raise(
                                ConnectionFailed,
                                "连接已断开：数据库连接被关闭或网络中断",
                                "monitor.connection_failed.notify_fail",
                                "ConnectionFailed handler failed");
                        }

                        ScheduleRetry(TimeSpan.FromSeconds(Math.Max(0, opt.ReconnectIntervalSeconds)), ct);
                    }
                    else
                    {
                        try { pending = await nextSignalTask.ConfigureAwait(false); }
                        catch (System.Exception ex)
                        {
                            _logger.Warn("DbConnectionMonitor", "monitor.pending_signal.read_fail", "Failed to read pending signal", ex);
                        }

                        // 主动重探保持 Connected：成功时勿再 Raise Reconnected，否则全页误刷
                        // Open 或 Scalar 失败仍走 catch，标成 Disconnected
                    }
                }
                finally
                {
                    conn.StateChange -= OnStateChange;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 仅服务停止退出；探测超时的 OCE 走下方失败分支，勿永久拆掉循环
                break;
            }
            catch (Exception ex)
            {
                var (reason, _) = DbConnectionDiagnostics.Classify(ex);
                var now = _time.GetUtcNow();
                var shouldLogWarn = !string.Equals(_lastProbeFailReason, reason, StringComparison.Ordinal)
                                    || now - _lastProbeFailAt >= ProbeFailLogThrottle;
                if (shouldLogWarn)
                {
                    _logger.Warn("DbConnectionMonitor", "monitor.probe.fail", "Database probe loop failed", ex, new
                    {
                        req.Kind,
                        reason
                    });
                    _lastProbeFailReason = reason;
                    _lastProbeFailAt = now;
                }

                req.Tcs?.TrySetResult(new DbProbeReport(req.Kind, Success: false, Reason: reason));

                var prevState = _state;
                _state = DbConnState.Disconnected;

                if (prevState != DbConnState.Disconnected || !_disconnectedNotified)
                {
                    _disconnectedNotified = true;
                    Raise(Disconnected, "monitor.disconnected.notify_fail", "Disconnected handler failed");
                    Raise(
                        ConnectionFailed,
                        reason,
                        "monitor.connection_failed.notify_fail",
                        "ConnectionFailed handler failed");
                }

                var delay = TimeSpan.FromSeconds(Math.Max(0, _dbConfig.Current.ReconnectIntervalSeconds));
                ScheduleRetry(delay, ct);
            }
            finally
            {
                if (conn is not null)
                {
                    try { await conn.CloseAsync().ConfigureAwait(false); }
                    catch (System.Exception ex)
                    {
                        _logger.Warn("DbConnectionMonitor", "monitor.conn.close_fail", "Failed to close probe connection", ex);
                    }

                    await conn.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    // 单测：替换 Open / SELECT 1，模拟「连上但标量失败」
    internal Func<PgOptions, CancellationToken, Task<NpgsqlConnection>>? OpenForTests { get; set; }

    internal Func<NpgsqlConnection, PgOptions, CancellationToken, Task>? ScalarForTests { get; set; }

    private async Task EnsureScalarAsync(NpgsqlConnection conn, PgOptions opt, CancellationToken ct)
    {
        try
        {
            if (ScalarForTests is { } hook)
            {
                await hook(conn, opt, ct).ConfigureAwait(false);
                return;
            }

            var timeout = TimeSpan.FromSeconds(Math.Max(1, opt.MonitorPingTimeoutSeconds));
            using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await using (CancelAfter.Schedule(pingCts, timeout, _time))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteScalarAsync(pingCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // 单次探测超时：转 TimeoutException，便于 Classify 与失败重试
            throw new TimeoutException("Database probe scalar timed out", ex);
        }
    }

    private static void StartPingLoop(
        NpgsqlConnection conn,
        PgOptions opt,
        TaskCompletionSource<object?> dropped,
        TimeProvider time,
        CancellationToken ct)
    {
        var intervalSeconds = Math.Max(0, opt.MonitorPingSeconds);
        if (intervalSeconds == 0)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var timeout = TimeSpan.FromSeconds(Math.Max(1, opt.MonitorPingTimeoutSeconds));

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !dropped.Task.IsCompleted)
            {
                try
                {
                    await Task.Delay(interval, time, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (ct.IsCancellationRequested || dropped.Task.IsCompleted)
                {
                    break;
                }

                try
                {
                    using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    await using (CancelAfter.Schedule(pingCts, timeout, time))
                    {
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT 1";
                        await cmd.ExecuteScalarAsync(pingCts.Token).ConfigureAwait(false);
                    }
                }
                catch
                {
                    dropped.TrySetResult(null);
                    break;
                }
            }
        }, ct);
    }

    // 逐个捕获，避免一个订阅者拖垮监控循环
    private void Raise(Action? handler, string eventName, string message)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action)subscriber).Invoke();
            }
            catch (Exception ex)
            {
                _logger.Warn("DbConnectionMonitor", eventName, message, ex);
            }
        }
    }

    private void Raise(Action<string>? handler, string arg, string eventName, string message)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((Action<string>)subscriber).Invoke(arg);
            }
            catch (Exception ex)
            {
                _logger.Warn("DbConnectionMonitor", eventName, message, ex);
            }
        }
    }
}
