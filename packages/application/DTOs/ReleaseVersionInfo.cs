namespace PacToolkits.Application.DTOs;

/// <summary>当前构建/发布版本快照（Desktop 安装目录 ReleaseManifest）</summary>
public sealed record ReleaseVersionInfo(
    string ProductVersion,
    string DesktopVersion,
    string AgentsVersion,
    string DbSchemaVersion,
    string BuildChannel,
    string BuildDate,
    string DesktopMinDbSchema,
    string DesktopMaxDbSchema)
{
    public static ReleaseVersionInfo Unknown { get; } = new(
        ProductVersion: "unknown",
        DesktopVersion: "unknown",
        AgentsVersion: "unknown",
        DbSchemaVersion: "unknown",
        BuildChannel: "unknown",
        BuildDate: "unknown",
        DesktopMinDbSchema: "unknown",
        DesktopMaxDbSchema: "unknown");
}
