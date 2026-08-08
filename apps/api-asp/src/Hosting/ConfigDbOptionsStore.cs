using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Api.Hosting;

/// <summary>
/// 从配置节 Postgres 提供 <see cref="IDbOptionsStore"/>
///
/// API 不写 Desktop 配置文件；Save 只更新进程内快照
/// </summary>
public sealed class ConfigDbOptionsStore : IDbOptionsStore
{
    private PgOptions _current;

    public ConfigDbOptionsStore(IOptions<PgOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _current = Clone(options.Value);
    }

    public string ConfigPath => "Postgres";

    public PgOptions LoadPgOptions() => Clone(_current);

    public Task SavePgOptionsAsync(PgOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        _current = Clone(options);
        return Task.CompletedTask;
    }

    private static PgOptions Clone(PgOptions src) => new()
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
        MonitorPingTimeoutSeconds = src.MonitorPingTimeoutSeconds,
    };
}
