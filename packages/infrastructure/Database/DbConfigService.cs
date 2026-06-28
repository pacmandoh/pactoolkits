using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

public sealed class DbConfigService : IDbConfigService
{
    private readonly IPgDataSourceFactory _factory;
    private readonly IDbOptionsStore _optionsStore;

    public PgOptions Current { get; }
    public string ConfigPath => _optionsStore.ConfigPath;
    public event EventHandler? Applied;

    public DbConfigService(
        IOptions<PgOptions> opt,
        IPgDataSourceFactory factory,
        IDbOptionsStore optionsStore)
    {
        _factory = factory;
        _optionsStore = optionsStore;

        Current = opt.Value;

        var postgres = _optionsStore.LoadPgOptions();
        CopyOptions(postgres, Current);

        _factory.Rebuild(Current);
    }

    public async Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct)
    {
        try
        {
            await using var conn = await PgConnectionFactory.OpenAsync(
                opt,
                ct,
                includeKeepAlive: false).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task ApplyAsync(PgOptions opt, CancellationToken ct = default)
    {
        await _optionsStore.SavePgOptionsAsync(CloneOptions(opt), ct).ConfigureAwait(false);

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
