using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 读取当前数据库 Schema 版本元数据
/// </summary>
public interface IDbSchemaVersionService
{
    Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct);

    Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct);
}
