using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IDatabaseEnvironmentSettingsService
{
    Task<DatabaseEnvironmentSettings> TryReadAsync(CancellationToken ct);

    Task<DatabaseEnvironmentSettings> TryReadAsync(PgOptions options, CancellationToken ct);
}
