namespace PacToolkits.Application.Abstractions;

public interface IDbSchemaVersionService
{
    Task<(bool ok, string? value, string? reason)> TryReadSchemaVersionAsync(CancellationToken ct);
}
