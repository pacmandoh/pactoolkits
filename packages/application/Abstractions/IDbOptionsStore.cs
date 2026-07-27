namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 定义 PostgreSQL 连接选项的本地持久化契约
/// </summary>
public interface IDbOptionsStore
{
    string ConfigPath { get; }
    PgOptions LoadPgOptions();
    Task SavePgOptionsAsync(PgOptions options, CancellationToken ct = default);
}
