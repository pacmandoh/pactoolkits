using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IDbSchemaMigrationService
{
    Task<DbSchemaMigrationPlan> GetPlanAsync(CancellationToken ct, string? targetVersion = null);

    Task<DbSchemaMigrationPlan> GetPlanAsync(
        PgOptions options,
        CancellationToken ct,
        string? targetVersion = null);

    Task<DbSchemaMigrationResult> EnsureUpToDateAsync(CancellationToken ct, string? targetVersion = null);

    Task<DbSchemaMigrationResult> EnsureUpToDateAsync(
        PgOptions options,
        CancellationToken ct,
        string? targetVersion = null);
}
