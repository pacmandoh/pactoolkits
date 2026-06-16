using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IDbSchemaVersionService
{
    Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(CancellationToken ct);

    Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct);
}
