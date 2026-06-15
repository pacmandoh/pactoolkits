using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Abstractions;

public interface IDbSchemaVersionService
{
    Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(CancellationToken ct);

    Task<DbSchemaVersionReadResult> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct);
}
