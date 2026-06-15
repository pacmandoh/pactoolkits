using PacToolkits.Application.DTOs;
using PacToolkits.Core;

namespace PacToolkits.Application.Abstractions;

public interface IDatabaseMigrationPolicyService
{
    DatabaseMigrationPolicyResult Evaluate(DatabaseMigrationEvaluationContext context);

    Task<DatabaseMigrationPolicyResult> EvaluateAsync(
        DatabaseMigrationTrigger trigger,
        DbSchemaCompatibility compatibility,
        string releaseChannel,
        string migrationPolicy,
        bool userConfirmed = false,
        bool ciMigrationAuthorized = false,
        CancellationToken ct = default);
}
