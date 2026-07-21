using System.Data;
using Npgsql;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// Npgsql 命令/参数构造辅助
///
/// 负责：统一 CommandTimeout 与参数附加；不打开连接、不执行业务 SQL
/// </summary>
public static class DbCmd
{
    public static NpgsqlCommand CreateCommand(
        this IDbConnection conn,
        string sql,
        int timeoutSeconds,
        IDbTransaction? tx = null)
    {
        var npgConn = (NpgsqlConnection)conn;
        var cmd = new NpgsqlCommand(sql, npgConn, tx as NpgsqlTransaction)
        {
            CommandTimeout = timeoutSeconds
        };
        return cmd;
    }

    public static NpgsqlParameter AddParam<T>(this NpgsqlCommand cmd, string name, T value)
    {
        var p = cmd.Parameters.AddWithValue(name, value!);
        return p;
    }
}
