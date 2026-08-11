using System.Data;
using Npgsql;

namespace PacToolkits.Infrastructure.Database;

/// <summary>
/// 统一 Npgsql 命令超时和参数构造规则
///
/// 不负责打开连接或执行业务 SQL
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
        var enlisted = tx ?? AmbientDbScope.Transaction;
        var cmd = new NpgsqlCommand(sql, npgConn, enlisted as NpgsqlTransaction)
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
