namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 当前 Postgres 连接配置的测试、应用与持久化
/// </summary>
public interface IDbConfigService
{
    PgOptions Current { get; }
    string ConfigPath { get; }

    event EventHandler? Applied;

    Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct);
    Task ApplyAsync(PgOptions opt, CancellationToken ct = default);
}
