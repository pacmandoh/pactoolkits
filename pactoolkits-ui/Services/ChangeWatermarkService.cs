using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Npgsql;
using pactoolkits_ui.Common;
using pactoolkits_ui.DataAccess;

namespace pactoolkits_ui.Services;

public interface IChangeWatermarkService : IDisposable
{
    event Action<string>? TopicChanged;
    void Start();
}

public sealed class ChangeWatermarkService : IChangeWatermarkService
{
    private const string NotifyChannel = "pactoolkits_change";

    private readonly IDb _db;
    private readonly IDbConfigService _dbConfig;
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

    public ChangeWatermarkService(IDb db, IDbConfigService dbConfig)
    {
        _db = db;
        _dbConfig = dbConfig;
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
            AppLog.Warn("ChangeWatermark", "watermark.dispose.cancel_fail", "Failed to cancel watermark service", ex);
        }

        try { _topics.Writer.TryComplete(); }
        catch (System.Exception ex)
        {
            AppLog.Warn("ChangeWatermark", "watermark.dispose.channel_close_fail", "Failed to close watermark channel", ex);
        }

        _cts.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshFromWatermarkAsync(emitOnBootstrap: false, ct).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                AppLog.Warn("ChangeWatermark", "watermark.poll.fail", "Watermark polling failed", ex);
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

                // Reason: Coalesce burst notifications into one watermark refresh.
                await Task.Delay(_notifyCoalesceWindow, ct).ConfigureAwait(false);
                while (_topics.Reader.TryRead(out _)) { }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RefreshFromWatermarkAsync(emitOnBootstrap: true, ct).ConfigureAwait(false);
            }
            catch (System.Exception ex)
            {
                AppLog.Warn("ChangeWatermark", "watermark.dispatch.fail", "Watermark dispatch failed", ex);
            }
        }
    }

    private async Task RefreshFromWatermarkAsync(bool emitOnBootstrap, CancellationToken ct)
    {
        var rows = await ReadWatermarksAsync(ct).ConfigureAwait(false);

        foreach (var (topic, version) in rows)
        {
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
                    shouldEmit = emitOnBootstrap;
                }
            }

            if (shouldEmit)
                TopicChanged?.Invoke(topic);
        }
    }

    private Task<List<(string Topic, long Version)>> ReadWatermarksAsync(CancellationToken ct)
        => _db.WithConnection(async (conn, token) =>
        {
            const string sql = """
                select topic, version
                from app_change_watermark
                order by topic
            """;

            await using var cmd = conn.CreateCommand(sql, timeoutSeconds: 4);
            var list = new List<(string Topic, long Version)>();

            await using var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1)) continue;
                var topic = reader.GetString(0);
                var version = reader.GetInt64(1);
                if (string.IsNullOrWhiteSpace(topic)) continue;
                list.Add((topic, version));
            }

            return list;
        }, ct);

    private string BuildListenConnectionString()
    {
        var opt = _dbConfig.Current;
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = opt.Host,
            Port = opt.Port,
            Database = opt.Database,
            Username = opt.Username,
            Password = opt.Password,
            Timeout = Math.Max(3, opt.ConnectTimeoutSeconds),
            KeepAlive = Math.Max(5, opt.KeepAliveSeconds),
            Pooling = false
        };
        return csb.ConnectionString;
    }
}
