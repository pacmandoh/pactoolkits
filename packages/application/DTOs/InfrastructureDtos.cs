namespace PacToolkits.Application.DTOs;

public sealed record DbTestResult(bool Ok, string Summary);

public sealed record DbSchemaMigrationResult(
    string? BeforeVersion,
    string? AfterVersion,
    int AppliedCount,
    int SkippedCount)
{
    public bool HasChanges => AppliedCount > 0;
}

public sealed record DbSchemaMigrationPlanItem(
    string Version,
    string FileName,
    bool Applied);

public sealed record DbSchemaMigrationPlan(
    string? CurrentVersion,
    string TargetVersion,
    bool BootstrapRequired,
    IReadOnlyList<DbSchemaMigrationPlanItem> Items)
{
    public int PendingCount => Items.Count(x => !x.Applied);
}

public enum DbProbeKind
{
    HealthCheck,
    Reconnect
}

public sealed record DbProbeReport(DbProbeKind Kind, bool Success, string? Reason);

public sealed record ReleaseVersionInfo(
    string ProductVersion,
    string DesktopVersion,
    string AgentInjectorAhkVersion,
    string DatabasePostgresVersion,
    string BuildChannel,
    string BuildDate,
    string DesktopMinDbSchema,
    string DesktopMaxDbSchema,
    string AgentInjectorAhkMinDbSchema,
    string AgentInjectorAhkMaxDbSchema,
    string DatabaseMigrationPolicy = DatabaseMigrationPolicies.StableOnly)
{
    public string SuiteVersion => ProductVersion;
    public string UiVersion => DesktopVersion;
    public string AgentVersion => AgentInjectorAhkVersion;
    public string DbSchemaVersion => DatabasePostgresVersion;
    public string UiMinDbSchema => DesktopMinDbSchema;
    public string UiMaxDbSchema => DesktopMaxDbSchema;
    public string AgentMinDbSchema => AgentInjectorAhkMinDbSchema;
    public string AgentMaxDbSchema => AgentInjectorAhkMaxDbSchema;

    public static ReleaseVersionInfo Unknown { get; } = new(
        ProductVersion: "unknown",
        DesktopVersion: "unknown",
        AgentInjectorAhkVersion: "unknown",
        DatabasePostgresVersion: "unknown",
        BuildChannel: "unknown",
        BuildDate: "unknown",
        DesktopMinDbSchema: "unknown",
        DesktopMaxDbSchema: "unknown",
        AgentInjectorAhkMinDbSchema: "unknown",
        AgentInjectorAhkMaxDbSchema: "unknown",
        DatabaseMigrationPolicy: DatabaseMigrationPolicies.StableOnly);
}
