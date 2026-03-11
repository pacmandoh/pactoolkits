using System;
using System.Collections.Generic;
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
            var candidates = GetCandidateVersionFiles();
            ReleaseVersionInfo? selected = null;
            foreach (var path in candidates)
            {
                var loaded = TryLoad(path);
                if (loaded is null)
                    continue;

                if (selected is null || CompareVersionInfo(loaded, selected) > 0)
                    selected = loaded;
            }

            return selected ?? ReleaseVersionInfo.Unknown;
        }
        catch
        {
            return ReleaseVersionInfo.Unknown;
        }
    }

    private static IEnumerable<string> GetCandidateVersionFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var direct = Path.Combine(AppContext.BaseDirectory, "version.generated.json");
        if (seen.Add(direct))
            yield return direct;

        var dir = AppContext.BaseDirectory;
        while (true)
        {
            var parent = Directory.GetParent(dir);
            if (parent is null)
                yield break;

            dir = parent.FullName;
            var candidate = Path.Combine(dir, "version.generated.json");
            if (seen.Add(candidate))
                yield return candidate;
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

    private static int CompareVersionInfo(ReleaseVersionInfo left, ReleaseVersionInfo right)
    {
        var db = CompareSemVerString(left.DbSchemaVersion, right.DbSchemaVersion);
        if (db != 0) return db;

        var suite = CompareSemVerString(left.SuiteVersion, right.SuiteVersion);
        if (suite != 0) return suite;

        var ui = CompareSemVerString(left.UiVersion, right.UiVersion);
        if (ui != 0) return ui;

        return string.Compare(left.BuildDate, right.BuildDate, StringComparison.Ordinal);
    }

    private static int CompareSemVerString(string left, string right)
    {
        var leftOk = DbSchemaCompat.TryParseSemVer(left, out var l);
        var rightOk = DbSchemaCompat.TryParseSemVer(right, out var r);
        if (leftOk && rightOk)
            return DbSchemaCompat.CompareSemVer(l, r);
        if (leftOk) return 1;
        if (rightOk) return -1;
        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
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
