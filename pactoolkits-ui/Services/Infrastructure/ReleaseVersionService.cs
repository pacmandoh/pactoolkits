using System;
using System.IO;
using System.Text.Json;

namespace pactoolkits_ui.Services.Infrastructure;

public interface IReleaseVersionService
{
    ReleaseVersionInfo Current { get; }
}

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

public sealed class ReleaseVersionService : IReleaseVersionService
{
    public ReleaseVersionInfo Current { get; }

    public ReleaseVersionService()
    {
        Current = LoadFromGeneratedFile();
    }

    private static ReleaseVersionInfo LoadFromGeneratedFile()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "version.generated.json");
            return TryLoad(path) ?? ReleaseVersionInfo.Unknown;
        }
        catch
        {
            return ReleaseVersionInfo.Unknown;
        }
    }

    private static ReleaseVersionInfo? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var compat = root.TryGetProperty("compat", out var compatElem) ? compatElem : default;
        var build = root.TryGetProperty("build", out var buildElem) ? buildElem : default;

        return new ReleaseVersionInfo(
            SuiteVersion: ReadString(root, "suiteVersion"),
            UiVersion: ReadString(root, "uiVersion"),
            AgentVersion: ReadString(root, "agentVersion"),
            DbSchemaVersion: ReadString(root, "dbSchemaVersion"),
            BuildChannel: ReadString(build, "channel"),
            BuildDate: ReadString(build, "date"),
            UiMinDbSchema: ReadString(compat, "uiMinDbSchema"),
            AgentMinDbSchema: ReadString(compat, "agentMinDbSchema"));
    }
    private static string ReadString(JsonElement elem, string name)
    {
        if (elem.ValueKind != JsonValueKind.Object)
            return "unknown";

        return elem.TryGetProperty(name, out var value)
            ? value.GetString() ?? "unknown"
            : "unknown";
    }
}
