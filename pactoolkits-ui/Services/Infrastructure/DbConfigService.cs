using System;
using pactoolkits_ui.DataAccess;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace pactoolkits_ui.Services.Infrastructure;

public interface IDbConfigService
{
    PgOptions Current { get; }
    string ConfigPath { get; }

    event EventHandler? Applied;

    Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct);
    Task SaveAndApplyAsync(PgOptions opt, CancellationToken ct = default);
}

public sealed class DbConfigService : IDbConfigService
{
    private readonly IPgDataSourceFactory _factory;
    private readonly IAppConfigStore _configStore;

    public PgOptions Current { get; }
    public string ConfigPath => _configStore.ConfigPath;
    public event EventHandler? Applied;

    public DbConfigService(
        IOptions<PgOptions> opt,
        IPgDataSourceFactory factory,
        IAppConfigStore configStore)
    {
        _factory = factory;
        _configStore = configStore;

        // Keep one mutable options instance for all consumers in this process.
        Current = opt.Value;

        // Unified config file is the single persistence source.
        var cfg = _configStore.Load();
        CopyOptions(cfg.Postgres, Current);

        // Build datasource once at startup from the effective options.
        _factory.Rebuild(Current);
    }

    public async Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct)
    {
        try
        {
            var csb = new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = opt.Host,
                Port = opt.Port,
                Database = opt.Database,
                Username = opt.Username,
                Password = opt.Password,
                SearchPath = "public",
                Timeout = opt.ConnectTimeoutSeconds
            };

            await using var conn = new Npgsql.NpgsqlConnection(csb.ToString());
            await conn.OpenAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveAndApplyAsync(PgOptions opt, CancellationToken ct = default)
    {
        // Persist first, then rebuild datasource, then publish Applied.
        // This order keeps disk/runtime/UI states aligned.
        var cfg = _configStore.Load();
        cfg.Postgres = CloneOptions(opt);
        await _configStore.SaveAsync(cfg, ct).ConfigureAwait(false);

        _factory.Rebuild(opt);

        CopyOptions(opt, Current);

        Applied?.Invoke(this, EventArgs.Empty);
    }

    private static PgOptions CloneOptions(PgOptions src) => new()
    {
        Host = src.Host,
        Port = src.Port,
        Database = src.Database,
        Username = src.Username,
        Password = src.Password,
        ConnectTimeoutSeconds = src.ConnectTimeoutSeconds,
        CommandTimeoutSeconds = src.CommandTimeoutSeconds,
        PoolSize = src.PoolSize,
        ReconnectIntervalSeconds = src.ReconnectIntervalSeconds,
        KeepAliveSeconds = src.KeepAliveSeconds,
        MonitorPingSeconds = src.MonitorPingSeconds,
        MonitorPingTimeoutSeconds = src.MonitorPingTimeoutSeconds
    };

    private static void CopyOptions(PgOptions src, PgOptions dst)
    {
        dst.Host = src.Host;
        dst.Port = src.Port;
        dst.Database = src.Database;
        dst.Username = src.Username;
        dst.Password = src.Password;
        dst.ConnectTimeoutSeconds = src.ConnectTimeoutSeconds;
        dst.CommandTimeoutSeconds = src.CommandTimeoutSeconds;
        dst.PoolSize = src.PoolSize;
        dst.ReconnectIntervalSeconds = src.ReconnectIntervalSeconds;
        dst.KeepAliveSeconds = src.KeepAliveSeconds;
        dst.MonitorPingSeconds = src.MonitorPingSeconds;
        dst.MonitorPingTimeoutSeconds = src.MonitorPingTimeoutSeconds;
    }
}
