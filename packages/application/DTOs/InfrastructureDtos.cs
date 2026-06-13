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

public enum DbProbeKind
{
    HealthCheck,
    Reconnect
}

public sealed record DbProbeReport(DbProbeKind Kind, bool Success, string? Reason);

public sealed record ReleaseVersionInfo(
    string SuiteVersion,
    string UiVersion,
    string AgentVersion,
    string DbSchemaVersion,
    string BuildChannel,
    string BuildDate,
    string UiMinDbSchema,
    string AgentMinDbSchema)
{
    public static ReleaseVersionInfo Unknown { get; } = new(
        SuiteVersion: "unknown",
        UiVersion: "unknown",
        AgentVersion: "unknown",
        DbSchemaVersion: "unknown",
        BuildChannel: "unknown",
        BuildDate: "unknown",
        UiMinDbSchema: "unknown",
        AgentMinDbSchema: "unknown");
}
