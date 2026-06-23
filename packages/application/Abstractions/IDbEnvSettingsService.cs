using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IDbEnvSettingsService
{
    Task<DbEnvSettings> TryReadAsync(CancellationToken ct);

    Task<DbEnvSettings> TryReadAsync(PgOptions options, CancellationToken ct);
}
