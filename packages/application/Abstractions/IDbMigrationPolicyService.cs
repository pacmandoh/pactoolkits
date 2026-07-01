using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Abstractions;

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
