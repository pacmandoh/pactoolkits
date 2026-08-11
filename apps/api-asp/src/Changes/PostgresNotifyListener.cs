using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Diagnostics;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Api.Changes;

/// <summary>
/// API 进程侧 LISTEN <c>pactoolkits_change</c>，分发到 <see cref="ChangeBus"/>
///
/// 现在 Desktop 仍可对本库各自 LISTEN；Pg 允许多会话同时听同一 channel
/// NOTIFY 只作唤醒：按 topic 记 pending，突发合并或丢弃；version 以 watermark 为准
/// </summary>
public sealed class PostgresNotifyListener : BackgroundService
{
    public const string NotifyChannel = "pactoolkits_change";

    private readonly IDbConfigService _dbConfig;
    private readonly ChangeBus _bus;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly ChangeListenOptions _options;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _coalesceWindow = TimeSpan.FromMilliseconds(120);

    public PostgresNotifyListener(
        IDbConfigService dbConfig,
        ChangeBus bus,
        IAppLogger logger,
        TimeProvider timeProvider,
        IOptions<ChangeListenOptions> options)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ListenEnabled)
        {
            _logger.Info("ChangeNotify", "listen.disabled", "PostgreSQL LISTEN host disabled");
            return;
        }

        // pending topic 集合与容量 1 唤醒：NOTIFY 洪峰不堆积字符串队列
        var pendingGate = new object();
        var pendingTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var coalesce = Task.Run(
            () => CoalesceLoopAsync(pendingGate, pendingTopics, wake.Reader, stoppingToken),
            stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(BuildListenConnectionString());
                await conn.OpenAsync(stoppingToken).ConfigureAwait(false);

                conn.Notification += (_, e) =>
                {
                    var topic = TopicFromPayload(e.Payload);
                    lock (pendingGate)
                    {
                        pendingTopics.Add(topic);
                    }

                    wake.Writer.TryWrite(true);
                };

                await using (var listen = new NpgsqlCommand($"LISTEN {NotifyChannel};", conn))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken).ConfigureAwait(false);
                }

                _logger.Info("ChangeNotify", "listen.up", "PostgreSQL LISTEN connected");

                while (!stoppingToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Warn("ChangeNotify", "listen.retry", "LISTEN disconnected; retrying", ex);
                try
                {
                    await Task.Delay(_retryDelay, _time, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        wake.Writer.TryComplete();
        try { await coalesce.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private async Task CoalesceLoopAsync(
        object pendingGate,
        HashSet<string> pendingTopics,
        ChannelReader<bool> wake,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _ = await wake.ReadAsync(ct).ConfigureAwait(false);
                await Task.Delay(_coalesceWindow, _time, ct).ConfigureAwait(false);
                while (wake.TryRead(out _))
                {
                }

                string[] topics;
                lock (pendingGate)
                {
                    topics = pendingTopics.ToArray();
                    pendingTopics.Clear();
                }

                // 合并窗内多 topic 均 Publish；客户端宜 GET watermarks 再按 topic 处理
                using var activity = PacActivities.Api.StartActivity("change.notify.publish");
                foreach (var topic in topics)
                {
                    _bus.Publish(topic);
                }
            }
            catch (OperationCanceledException)
            {
                break;
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
            Pooling = false,
        };
        return csb.ConnectionString;
    }

    private static string TopicFromPayload(string? payload)
    {
        var t = (payload ?? string.Empty).Trim();
        return t.Length == 0 ? "inventory" : t;
    }
}
