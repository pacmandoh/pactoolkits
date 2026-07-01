using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IDbSchemaVersionService
{
    Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct);

    Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct);
}
