namespace PacToolkits.Application.Abstractions;

/// <summary>当前 Postgres 连接：读取与探测</summary>
public interface IDbConfigService
{
    PgOptions Current { get; }

    Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct);
}
