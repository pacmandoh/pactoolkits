using System.Text.Json;
using PacToolkits.Core;

namespace PacToolkits.Tests.Shared;

/// <summary>
/// PG 集成用 schema 区间：Compatible 跟仓库 release-manifest，BelowMinimum 相对现场库推算
/// </summary>
internal static class ManifestDbSchema
{
    public readonly record struct Bounds(string Min, string Max, string Target);

    public static Bounds CompatibleBounds()
    {
        var path = FindManifestPath();
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (!root.TryGetProperty("components", out var components)
            || components.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"release-manifest missing components: {path}");
        }

        if (!components.TryGetProperty("api", out var api)
            || api.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"release-manifest missing api: {path}");
        }

        if (!components.TryGetProperty("database", out var databaseRoot)
            || !databaseRoot.TryGetProperty("postgres", out var postgres)
            || postgres.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"release-manifest missing database.postgres: {path}");
        }

        var min = RequireString(api, "minDbSchema", path);
        var max = RequireString(api, "maxDbSchema", path);
        var target = RequireString(postgres, "version", path);
        return new Bounds(min, max, target);
    }

    /// <summary>构造现场库一定 BelowMinimum 的闭区间（min/max/target 均高于 live）</summary>
    public static Bounds BelowMinimumBounds(string liveSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(liveSchema);
        var min = NextPatch(liveSchema.Trim());
        var max = NextPatch(min);
        return new Bounds(min, max, max);
    }

    public static string NextPatch(string version)
    {
        if (!SemVer.TryParse(version, out var parsed) || parsed.PreRelease is not null)
        {
            throw new ArgumentException(
                $"expected release SemVer X.Y.Z, got '{version}'",
                nameof(version));
        }

        return $"{parsed.Major}.{parsed.Minor}.{parsed.Patch + 1}";
    }

    private static string RequireString(JsonElement obj, string name, string path)
    {
        if (!obj.TryGetProperty(name, out var el)
            || el.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(el.GetString()))
        {
            throw new InvalidOperationException($"release-manifest missing {name}: {path}");
        }

        return el.GetString()!.Trim();
    }

    private static string FindManifestPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "release-manifest.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "release-manifest.json not found walking up from " + AppContext.BaseDirectory);
    }
}
