using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Abstractions;

public interface IDatabaseEnvironmentSettingsService
{
    Task<DatabaseEnvironmentSettings> TryReadAsync(CancellationToken ct);

    Task<DatabaseEnvironmentSettings> TryReadAsync(PgOptions options, CancellationToken ct);
}
