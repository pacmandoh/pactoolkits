using Npgsql;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 按 <c>PgOptions</c> 构造/打开一次性 Npgsql 连接（不经运行时池）
/// 供配置探测、LISTEN 等旁路路径使用
/// </summary>
internal static class PgConnectionFactory
{
    public static string BuildConnectionString(PgOptions opt, bool includeKeepAlive = true, int? timeoutSeconds = null)
    {
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = opt.Host,
            Port = opt.Port,
            Database = opt.Database,
            Username = opt.Username,
            Password = opt.Password,
            SearchPath = "public",
            Timeout = timeoutSeconds ?? opt.ConnectTimeoutSeconds
        };

        if (includeKeepAlive)
        {
            csb.KeepAlive = opt.KeepAliveSeconds;
        }

        return csb.ToString();
    }

    public static async Task<NpgsqlConnection> OpenAsync(
        PgOptions opt,
        CancellationToken ct,
        bool includeKeepAlive = true,
        int? timeoutSeconds = null)
    {
        var conn = new NpgsqlConnection(BuildConnectionString(opt, includeKeepAlive, timeoutSeconds));
        await conn.OpenAsync(ct).ConfigureAwait(false);
        return conn;
    }
}
