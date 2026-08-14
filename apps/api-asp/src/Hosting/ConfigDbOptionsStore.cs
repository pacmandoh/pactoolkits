using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Api.Hosting;

/// <summary>从配置节 Postgres 提供连接选项</summary>
public sealed class ConfigDbOptionsStore : IDbOptionsStore
{
    private readonly PgOptions _current;

    public ConfigDbOptionsStore(IOptions<PgOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _current = Clone(options.Value);
    }

    public PgOptions LoadPgOptions() => Clone(_current);

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
        KeepAliveSeconds = src.KeepAliveSeconds,
    };
}
