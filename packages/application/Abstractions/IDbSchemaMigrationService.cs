using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IDbSchemaMigrationService
{
    Task<DbSchemaMigrationPlan> GetPlanAsync(CancellationToken ct, string? targetVersion = null);

    Task<DbSchemaMigrationPlan> GetPlanAsync(
        PgOptions options,
        CancellationToken ct,
        string? targetVersion = null);

    Task<DbSchemaMigrationResult> MigrateUpToDateAsync(CancellationToken ct, string? targetVersion = null);

    Task<DbSchemaMigrationResult> MigrateUpToDateAsync(
        PgOptions options,
        CancellationToken ct,
        string? targetVersion = null);
}
