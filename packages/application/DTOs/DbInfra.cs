namespace PacToolkits.Application.DTOs;

public sealed record DbTestResult(bool Ok, string Summary);

public sealed record DbSchemaMigrationResult(
    string? BeforeVersion,
    string? AfterVersion,
    int AppliedCount,
    int SkippedCount);

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
    string AgentsVersion,
    string DbSchemaVersion,
    string BuildChannel,
    string BuildDate,
    string DesktopMinDbSchema,
    string DesktopMaxDbSchema,
    string AgentsMinDbSchema,
    string AgentsMaxDbSchema,
    string DbMigrationPolicy = DbMigrationPolicies.StableOnly)
{
    public static ReleaseVersionInfo Unknown { get; } = new(
        ProductVersion: "unknown",
        DesktopVersion: "unknown",
        AgentsVersion: "unknown",
        DbSchemaVersion: "unknown",
        BuildChannel: "unknown",
        BuildDate: "unknown",
        DesktopMinDbSchema: "unknown",
        DesktopMaxDbSchema: "unknown",
        AgentsMinDbSchema: "unknown",
        AgentsMaxDbSchema: "unknown",
        DbMigrationPolicy: DbMigrationPolicies.StableOnly);
}
