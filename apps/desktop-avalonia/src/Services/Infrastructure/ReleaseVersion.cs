using System;
using System.IO;
using System.Text.Json;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>定义当前发布版本和组件版本的查询契约</summary>
public interface IReleaseVersionService
{
    ReleaseVersionInfo Current { get; }
}

/// <summary>读取当前构建/发布版本信息</summary>
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
            var path = Path.Combine(AppContext.BaseDirectory, "ReleaseManifest.json");
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

        if (!root.TryGetProperty("schemaVersion", out var schemaElem)
            || schemaElem.GetInt32() != 2)
        {
            return null;
        }

        var product = root.TryGetProperty("product", out var productElem) ? productElem : default;
        var components = root.TryGetProperty("components", out var componentsElem) ? componentsElem : default;
        var desktop = components.ValueKind == JsonValueKind.Object
            && ReleaseManifestDesktop.TryGetAvalonia(components, out var desktopElem)
            ? desktopElem
            : default;
        var agents = components.ValueKind == JsonValueKind.Object
            && components.TryGetProperty("agents", out var agentsElem)
            ? agentsElem
            : default;
        var database = components.ValueKind == JsonValueKind.Object
            && components.TryGetProperty("database", out var databaseRoot)
            && databaseRoot.TryGetProperty("postgres", out var postgresElem)
            ? postgresElem
            : default;
        var release = root.TryGetProperty("release", out var releaseElem) ? releaseElem : default;

        return new ReleaseVersionInfo(
            ProductVersion: ReadString(product, "version"),
            DesktopVersion: ReadString(desktop, "version"),
            AgentsVersion: ReadString(agents, "version"),
            DbSchemaVersion: ReadString(database, "version"),
            BuildChannel: ReadString(release, "channel"),
            BuildDate: ReadString(release, "date"),
            DesktopMinDbSchema: ReadString(desktop, "minDbSchema"),
            DesktopMaxDbSchema: ReadString(desktop, "maxDbSchema"),
            AgentsMinDbSchema: ReadString(agents, "minDbSchema"),
            AgentsMaxDbSchema: ReadString(agents, "maxDbSchema"));
    }

    private static string ReadString(JsonElement elem, string name)
    {
        if (elem.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return elem.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
