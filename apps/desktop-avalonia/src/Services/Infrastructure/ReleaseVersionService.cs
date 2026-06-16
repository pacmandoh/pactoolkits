using System;
using System.IO;
using System.Text.Json;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public interface IReleaseVersionService
{
    ReleaseVersionInfo Current { get; }
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
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        if (root.TryGetProperty("schemaVersion", out var schemaElem)
            && schemaElem.GetInt32() == 2)
        {
            var product = root.TryGetProperty("product", out var productElem) ? productElem : default;
            var components = root.TryGetProperty("components", out var componentsElem) ? componentsElem : default;
            var desktop = components.ValueKind == JsonValueKind.Object
                && components.TryGetProperty("desktop", out var desktopElem)
                ? desktopElem
                : default;
            var agent = components.ValueKind == JsonValueKind.Object
                && components.TryGetProperty("agent-injector-ahk", out var agentElem)
                ? agentElem
                : default;
            var database = components.ValueKind == JsonValueKind.Object
                && components.TryGetProperty("database-postgres", out var databaseElem)
                ? databaseElem
                : default;
            var release = root.TryGetProperty("release", out var releaseElem) ? releaseElem : default;

            return new ReleaseVersionInfo(
                ProductVersion: ReadString(product, "version"),
                DesktopVersion: ReadString(desktop, "version"),
                AgentInjectorAhkVersion: ReadString(agent, "version"),
                DatabasePostgresVersion: ReadString(database, "version"),
                BuildChannel: ReadString(release, "channel"),
                BuildDate: ReadString(release, "date"),
                DesktopMinDbSchema: ReadString(desktop, "minDbSchema"),
                DesktopMaxDbSchema: ReadString(desktop, "maxDbSchema"),
                AgentInjectorAhkMinDbSchema: ReadString(agent, "minDbSchema"),
                AgentInjectorAhkMaxDbSchema: ReadString(agent, "maxDbSchema"),
                DatabaseMigrationPolicy: ReadString(database, "migrationPolicy"));
        }

        var compat = root.TryGetProperty("compat", out var compatElem) ? compatElem : default;
        var build = root.TryGetProperty("build", out var buildElem) ? buildElem : default;

        return new ReleaseVersionInfo(
            ProductVersion: ReadString(root, "suiteVersion"),
            DesktopVersion: ReadString(root, "uiVersion"),
            AgentInjectorAhkVersion: ReadString(root, "agentVersion"),
            DatabasePostgresVersion: ReadString(root, "dbSchemaVersion"),
            BuildChannel: ReadString(build, "channel"),
            BuildDate: ReadString(build, "date"),
            DesktopMinDbSchema: ReadString(compat, "uiMinDbSchema"),
            DesktopMaxDbSchema: ReadString(compat, "uiMaxDbSchema"),
            AgentInjectorAhkMinDbSchema: ReadString(compat, "agentMinDbSchema"),
            AgentInjectorAhkMaxDbSchema: ReadString(compat, "agentMaxDbSchema"),
            DatabaseMigrationPolicy: DatabaseMigrationPolicies.StableOnly);
    }

    private static string ReadString(JsonElement elem, string name)
    {
        if (elem.ValueKind != JsonValueKind.Object)
        {
            return "unknown";
        }

        return elem.TryGetProperty(name, out var value)
            ? value.GetString() ?? "unknown"
            : "unknown";
    }
}
