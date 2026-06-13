using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IDbSchemaMigrationService
{
    Task<DbSchemaMigrationResult> EnsureUpToDateAsync(CancellationToken ct, string? targetVersion = null);
}
