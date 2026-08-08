using Microsoft.Extensions.Options;
using Npgsql;
using PacToolkits.Application.Abstractions;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Api.Changes;

/// <summary>
/// API 进程侧 LISTEN <c>pactoolkits_change</c>，分发到 <see cref="ChangeBus"/>
///
/// 过渡期 Desktop 仍可对本库各自 LISTEN；Pg 允许多会话同时听同一 channel
/// </summary>
public sealed class PostgresNotifyListener : BackgroundService
{
    public const string NotifyChannel = "pactoolkits_change";

    private readonly IDbConfigService _dbConfig;
    private readonly ChangeBus _bus;
    private readonly IAppLogger _logger;
    private readonly ChangeListenOptions _options;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _coalesceWindow = TimeSpan.FromMilliseconds(120);

    public PostgresNotifyListener(
        IDbConfigService dbConfig,
        ChangeBus bus,
        IAppLogger logger,
        IOptions<ChangeListenOptions> options)
    {
        _dbConfig = dbConfig ?? throw new ArgumentNullException(nameof(dbConfig));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.ListenEnabled)
        {
            _logger.Info("ChangeNotify", "listen.disabled", "PostgreSQL LISTEN host disabled");
            return;
        }

        var pending = ChannelCreate();
        var coalesce = Task.Run(() => CoalesceLoopAsync(pending.Reader, stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(BuildListenConnectionString());
                await conn.OpenAsync(stoppingToken).ConfigureAwait(false);

                conn.Notification += (_, e) =>
                {
                    pending.Writer.TryWrite(TopicFromPayload(e.Payload));
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
                    await Task.Delay(_retryDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        pending.Writer.TryComplete();
        try { await coalesce.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    private async Task CoalesceLoopAsync(
        System.Threading.Channels.ChannelReader<string> reader,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var first = await reader.ReadAsync(ct).ConfigureAwait(false);
                await Task.Delay(_coalesceWindow, ct).ConfigureAwait(false);

                // 合并窗内多 topic 均 Publish；客户端宜 GET watermarks 再按 topic 处理
                var topics = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first };
                while (reader.TryRead(out var topic))
                {
                    topics.Add(topic);
                }

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

    private static System.Threading.Channels.Channel<string> ChannelCreate()
        => System.Threading.Channels.Channel.CreateUnbounded<string>(
            new System.Threading.Channels.UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });

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
