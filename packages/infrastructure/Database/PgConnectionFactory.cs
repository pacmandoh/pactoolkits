using Npgsql;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Infrastructure.Database;

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
