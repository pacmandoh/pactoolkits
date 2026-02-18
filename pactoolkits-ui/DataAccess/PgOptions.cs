using Npgsql;

namespace pactoolkits_ui.DataAccess;

public sealed class PgOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "postgres";
    public string Username { get; set; } = "postgres";
    public string Password { get; set; } = "";

    public int ConnectTimeoutSeconds { get; set; } = 6;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public int PoolSize { get; set; } = 20;

    public int ReconnectIntervalSeconds { get; set; } = 2;

    public int KeepAliveSeconds { get; set; } = 5;

    public int MonitorPingSeconds { get; set; } = 5;
    public int MonitorPingTimeoutSeconds { get; set; } = 3;

    public string BuildConnectionString()
    {
        var csb = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = Password,
            Pooling = true,
            MaxPoolSize = PoolSize,
            Timeout = ConnectTimeoutSeconds,
            KeepAlive = KeepAliveSeconds
        };

        return csb.ConnectionString;
    }
}
