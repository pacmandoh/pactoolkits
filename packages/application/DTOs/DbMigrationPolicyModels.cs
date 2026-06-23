using PacToolkits.Core;

namespace PacToolkits.Application.DTOs;

public enum DbMigrationTrigger
{
    Startup,
    Reconnect,
    SettingsManual,
    ExternalDeploy,
}

public enum DbMigrationDecision
{
    Allowed,
    RequiresConfirmation,
    ReadOnlyRequired,
}

public static class DbMigrationPolicies
{
    public const string StableOnly = "stable-only";
    public const string Manual = "manual";
    public const string IsolatedBeta = "isolated-beta";
}

public sealed record DbEnvSettings(
    string Environment,
    bool AllowBetaMigrations)
{
    public static DbEnvSettings ProductionDefaults { get; } = new("production", false);

    public bool IsIsolated =>
        string.Equals(Environment, "isolated", StringComparison.OrdinalIgnoreCase);
}

public sealed record DbMigrationEvaluationContext(
    DbMigrationTrigger Trigger,
    DbSchemaCompatibility Compatibility,
    string ReleaseChannel,
    string MigrationPolicy,
    DbEnvSettings EnvironmentSettings,
    bool UserConfirmed,
    bool CiMigrationAuthorized);

public sealed record DbMigrationPolicyResult(
    DbMigrationDecision Decision,
    string Reason,
    bool RunMigration);
