using System;
using System.IO;
using System.Text.Json;

namespace pactoolkits_ui.Services;

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
    string AgentMinUi,
    string UiMinAgent)
{
    public static ReleaseVersionInfo Unknown { get; } = new(
        SuiteVersion: "unknown",
        UiVersion: "unknown",
        AgentVersion: "unknown",
        DbSchemaVersion: "unknown",
        BuildChannel: "unknown",
        BuildDate: "unknown",
        AgentMinUi: "unknown",
        UiMinAgent: "unknown");
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
            if (!File.Exists(path))
                return ReleaseVersionInfo.Unknown;

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
                AgentMinUi: ReadString(compat, "agentMinUi"),
                UiMinAgent: ReadString(compat, "uiMinAgent"));
        }
        catch
        {
            return ReleaseVersionInfo.Unknown;
        }
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
