using System.Threading.Channels;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;


namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 数据库连接存活监控与探测
///
/// 负责：后台探测循环、断线/重连事件、按需 <c>ProbeAsync</c>
/// 不修改连接配置；连接串来自 <c>IDbConfigService</c>
/// </summary>
public sealed class DbConnectionMonitorService : IDbConnectionMonitorService
{
    private readonly IDbConfigService _dbConfig;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _cts = new();

    private readonly Channel<ProbeRequest> _signals = Channel.CreateUnbounded<ProbeRequest>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private readonly record struct ProbeRequest(DbProbeKind Kind, TaskCompletionSource<DbProbeReport>? Tcs);

    private Task? _loop;

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

    public DbConnectionMonitorService(IDbConfigService dbConfig, IAppLogger logger)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        _loop ??= Task.Run(() => RunAsync(_cts.Token));
    }

    private void EnqueueSignal()
    {
        _signals.Writer.TryWrite(new ProbeRequest(DbProbeKind.Reconnect, null));
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
                await Task.Delay(delay, ct).ConfigureAwait(false);
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

            NpgsqlConnection? conn = null;
            try
            {
                var opt = _dbConfig.Current;

                conn = await PgConnectionFactory.OpenAsync(opt, ct).ConfigureAwait(false);

                Interlocked.Exchange(ref _retryScheduled, 0);
                _lastProbeFailReason = string.Empty;
                _lastProbeFailAt = DateTimeOffset.MinValue;

                var prev = _state;
                _state = DbConnState.Connected;
                _disconnectedNotified = false;

                if (prev != DbConnState.Connected)
                {
                    Reconnected?.Invoke();
                }

                await CompleteProbeAsync(_logger, req, conn, opt, ct).ConfigureAwait(false);

                var dropped = new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                StartPingLoop(conn, opt, dropped, ct);


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

                    // 取消挂起的 ReadAsync，避免它吞掉下一次 Signal
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
                            Disconnected?.Invoke();
                            ConnectionFailed?.Invoke("连接已断开：数据库连接被关闭或网络中断");
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
                    }
                }
                finally
                {
                    conn.StateChange -= OnStateChange;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                var (reason, _) = DbConnectionDiagnostics.Classify(ex);
                var now = DateTimeOffset.UtcNow;
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
                    Disconnected?.Invoke();
                    ConnectionFailed?.Invoke(reason);
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

    private static async Task CompleteProbeAsync(
        IAppLogger logger,
        ProbeRequest req,
        NpgsqlConnection conn,
        PgOptions opt,
        TaskCompletionSource<object?>? dropped,
        CancellationToken ct)
    {
        if (req.Tcs is null)
        {
            return;
        }

        var timeout = TimeSpan.FromSeconds(Math.Max(1, opt.MonitorPingTimeoutSeconds));
        try
        {
            using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            pingCts.CancelAfter(timeout);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(pingCts.Token).ConfigureAwait(false);

            req.Tcs.TrySetResult(new DbProbeReport(req.Kind, true, null));
        }
        catch (Exception ex)
        {
            var (reason, _) = DbConnectionDiagnostics.Classify(ex);
            logger.Warn("DbConnectionMonitor", "monitor.probe.scalar_fail", "DB probe scalar check failed", ex, new
            {
                req.Kind,
                reason
            });
            req.Tcs.TrySetResult(new DbProbeReport(req.Kind, false, reason));

            dropped?.TrySetResult(null);
        }
    }

    private static Task CompleteProbeAsync(
        IAppLogger logger,
        ProbeRequest req,
        NpgsqlConnection conn,
        PgOptions opt,
        CancellationToken ct)
        => CompleteProbeAsync(logger, req, conn, opt, dropped: null, ct);

    private static void StartPingLoop(NpgsqlConnection conn, PgOptions opt,
        TaskCompletionSource<object?> dropped, CancellationToken ct)
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
                    await Task.Delay(interval, ct).ConfigureAwait(false);
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
                    pingCts.CancelAfter(timeout);

                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT 1";
                    await cmd.ExecuteScalarAsync(pingCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    dropped.TrySetResult(null);
                    break;
                }
            }
        }, ct);
    }
}
