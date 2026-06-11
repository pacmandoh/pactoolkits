using System.Data;
using Npgsql;

namespace pactoolkits_ui.DataAccess;

public static class DbCmd
{
    public static NpgsqlCommand CreateCommand(
        this IDbConnection conn,
        string sql,
        int timeoutSeconds,
        IDbTransaction? tx = null)
    {
        var npgConn = (NpgsqlConnection)conn;
        var cmd = new NpgsqlCommand(sql, npgConn, tx as NpgsqlTransaction);
        cmd.CommandTimeout = timeoutSeconds;
        return cmd;
    }

    public static NpgsqlParameter AddParam<T>(this NpgsqlCommand cmd, string name, T value)
    {
        var p = cmd.Parameters.AddWithValue(name, value!);
        return p;
    }
}
