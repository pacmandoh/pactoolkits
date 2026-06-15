using PacToolkits.Core;

namespace PacToolkits.Application.DTOs;

public enum DatabaseMigrationTrigger
{
    Startup,
    Reconnect,
    SettingsManual,
    ExternalDeploy,
}

public enum DatabaseMigrationDecision
{
    Allowed,
    RequiresConfirmation,
    ReadOnlyRequired,
}

public static class DatabaseMigrationPolicies
{
    public const string StableOnly = "stable-only";
    public const string Manual = "manual";
    public const string IsolatedBeta = "isolated-beta";
}

public sealed record DatabaseEnvironmentSettings(
    string Environment,
    bool AllowBetaMigrations)
{
    public static DatabaseEnvironmentSettings ProductionDefaults { get; } = new("production", false);

    public bool IsIsolated =>
        string.Equals(Environment, "isolated", StringComparison.OrdinalIgnoreCase);
}

public sealed record DatabaseMigrationEvaluationContext(
    DatabaseMigrationTrigger Trigger,
    DbSchemaCompatibility Compatibility,
    string ReleaseChannel,
    string MigrationPolicy,
    DatabaseEnvironmentSettings EnvironmentSettings,
    bool UserConfirmed,
    bool CiMigrationAuthorized);

public sealed record DatabaseMigrationPolicyResult(
    DatabaseMigrationDecision Decision,
    string Reason,
    bool ShouldExecuteMigration);
