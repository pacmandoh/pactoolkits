using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// Schema 迁移计划与执行（升到目标版本）
/// </summary>
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
