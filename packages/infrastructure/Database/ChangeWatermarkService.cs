using System.Threading.Channels;
using Npgsql;
using PacToolkits.Application.Abstractions;

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
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, long> _versions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(20);
    private readonly TimeSpan _listenRetryDelay = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _notifyCoalesceWindow = TimeSpan.FromMilliseconds(120);

    private readonly Channel<string> _topics = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private Task? _listenLoop;
    private Task? _pollLoop;
    private Task? _dispatchLoop;

    public event Action<string>? TopicChanged;

    public ChangeWatermarkService(
        IChangeWatermarkRepo watermarks,
        IDbConfigService dbConfig,
        IAppLogger logger)
    {
        _watermarks = watermarks ?? throw new ArgumentNullException(nameof(watermarks));
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start()
    {
        _dispatchLoop ??= Task.Run(() => DispatchLoopAsync(_cts.Token));
        _pollLoop ??= Task.Run(() => PollLoopAsync(_cts.Token));
        _listenLoop ??= Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public void Dispose()
    {
        try { _cts.Cancel(); }
        catch (System.Exception ex)
        {
            _logger.Warn("ChangeWatermark", "watermark.dispose.cancel_fail", "Failed to cancel watermark service", ex);
        }

        try { _topics.Writer.TryComplete(); }
        catch (System.Exception ex)
        {
            _logger.Warn("ChangeWatermark", "watermark.dispose.channel_close_fail", "Failed to close watermark channel", ex);
        }

        _cts.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // poll：首次见到 topic 不刷页（避免启动连环刷新）
                await RefreshFromWatermarkAsync(emitOnBootstrap: false, ct).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                _logger.Warn("ChangeWatermark", "watermark.poll.fail", "Watermark polling failed", ex);
            }

            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(BuildListenConnectionString());
                await conn.OpenAsync(ct).ConfigureAwait(false);

                conn.Notification += (_, e) =>
                {
                    var payload = (e.Payload).Trim();
                    var topic = string.IsNullOrWhiteSpace(payload) ? "inventory" : payload;
                    _topics.Writer.TryWrite(topic);
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
                    await Task.Delay(_listenRetryDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ = await _topics.Reader.ReadAsync(ct).ConfigureAwait(false);

                // 短窗内合并突发 NOTIFY，避免一次水位刷新打成多次
                await Task.Delay(_notifyCoalesceWindow, ct).ConfigureAwait(false);
                while (_topics.Reader.TryRead(out _)) { }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                // NOTIFY/dispatch：首次见到 topic 也要 emit
                await RefreshFromWatermarkAsync(emitOnBootstrap: true, ct).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                _logger.Warn("ChangeWatermark", "watermark.dispatch.fail", "Watermark dispatch failed", ex);
            }
        }
    }

    private async Task RefreshFromWatermarkAsync(bool emitOnBootstrap, CancellationToken ct)
    {
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
                        shouldEmit = true;
                    }
                }
                else
                {
                    _versions[topic] = version;
                    // 首次见到 topic：poll 启动不刷页；dispatch/NOTIFY 路径才 emit
                    shouldEmit = emitOnBootstrap;
                }
            }

            if (shouldEmit)
            {
                TopicChanged?.Invoke(topic);
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
