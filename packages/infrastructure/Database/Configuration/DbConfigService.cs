using Microsoft.Extensions.Options;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// PostgreSQL 连接配置的加载与探测
///
/// 启动时读选项并重建数据源
/// </summary>
public sealed class DbConfigService : IDbConfigService
{
    private readonly IPgDataSourceFactory _factory;

    public PgOptions Current { get; }

    public DbConfigService(
        IOptions<PgOptions> opt,
        IPgDataSourceFactory factory,
        IDbOptionsStore optionsStore)
    {
        _factory = factory;

        Current = opt.Value;

        var postgres = optionsStore.LoadPgOptions();
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
        dst.KeepAliveSeconds = src.KeepAliveSeconds;
    }
}
