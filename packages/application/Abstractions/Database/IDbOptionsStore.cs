namespace PacToolkits.Application.Abstractions;

/// <summary>读取 Postgres 连接选项</summary>
public interface IDbOptionsStore
{
    PgOptions LoadPgOptions();
}
