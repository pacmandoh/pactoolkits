namespace PacToolkits.Application.DTOs;

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
    string AgentsMaxDbSchema)
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
        AgentsMaxDbSchema: "unknown");
}
