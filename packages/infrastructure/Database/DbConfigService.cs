using Microsoft.Extensions.Options;
using Npgsql;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

public sealed class DbConfigService : IDbConfigService
{
    private readonly IPgDataSourceFactory _factory;
    private readonly IPostgresConfigStore _configStore;

    public PgOptions Current { get; }
    public string ConfigPath => _configStore.ConfigPath;
    public event EventHandler? Applied;

    public DbConfigService(
        IOptions<PgOptions> opt,
        IPgDataSourceFactory factory,
        IPostgresConfigStore configStore)
    {
        _factory = factory;
        _configStore = configStore;

        Current = opt.Value;

        var postgres = _configStore.LoadPostgresOptions();
        CopyOptions(postgres, Current);

        _factory.Rebuild(Current);
    }

    public async Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct)
    {
        try
        {
            var csb = new NpgsqlConnectionStringBuilder
            {
                Host = opt.Host,
                Port = opt.Port,
                Database = opt.Database,
                Username = opt.Username,
                Password = opt.Password,
                SearchPath = "public",
                Timeout = opt.ConnectTimeoutSeconds
            };

            await using var conn = new NpgsqlConnection(csb.ToString());
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
        await _configStore.SavePostgresOptionsAsync(CloneOptions(opt), ct).ConfigureAwait(false);

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
