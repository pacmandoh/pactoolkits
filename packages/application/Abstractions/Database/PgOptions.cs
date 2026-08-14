namespace PacToolkits.Application.Abstractions;

/// <summary>
/// Postgres 连接与池超时参数
/// </summary>
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
    public int KeepAliveSeconds { get; set; } = 5;
}
