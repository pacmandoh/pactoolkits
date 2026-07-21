using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 按通道、策略与环境判定是否允许 Schema 迁移
/// </summary>
public interface IDbMigrationPolicyService
{
    DbMigrationOutcome Evaluate(DbMigrationEvaluationContext context);

    Task<DbMigrationOutcome> EvaluateAsync(
        DbMigrationTrigger trigger,
        DbSchemaCompatibility compatibility,
        string releaseChannel,
        string migrationPolicy,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false,
        CancellationToken ct = default);
}
