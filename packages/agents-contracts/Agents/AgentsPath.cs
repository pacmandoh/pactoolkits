using System.Text.Json;

namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Host 可执行路径解析结果来源（配置路径 / 标准布局 / 缺失）
/// </summary>
public enum HostExecutableResolutionSource
{
    Configured,
    Standard,
    Missing,
}

/// <summary>
/// Host 路径解析结果（含写入配置时应使用的存储路径）
/// </summary>
public sealed record HostExecutableResolution(
    string? ResolvedPath,
    string StoredPath,
    HostExecutableResolutionSource Source);

/// <summary>
/// Host / module.json 路径解析与 Main Tools→Host 配置迁移判定
/// </summary>
public static class AgentsPath
{
    public static string? TryReadModuleEntryFileName(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!entry.TryGetProperty("windows-x64", out var win) || win.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var name = win.GetString();
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static string? TryResolveModuleEntryPath(string agentsDir, string moduleId)
    {
        if (string.IsNullOrWhiteSpace(agentsDir) || string.IsNullOrWhiteSpace(moduleId))
        {
            return null;
        }

        var fileName = TryReadModuleEntryFileName(AgentsPaths.ModuleManifestPath(agentsDir, moduleId));
        return fileName is null
            ? null
            : Path.Combine(AgentsPaths.ModuleDir(agentsDir, moduleId), fileName);
    }

    public static string? TryReadModuleVersion(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = version.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static HostExecutableResolution ResolveHost(string? configuredPath, string baseDirectory)
    {
        var configured = NormalizeStoredPath(configuredPath);
        var standardStored = AgentsPaths.HostExecutable;
        var resolvedStandard = ResolvePath(standardStored, baseDirectory);
        var standardExists = resolvedStandard is not null && File.Exists(resolvedStandard);

        // Main-only：即使本机尚无 Host 二进制（如 macOS 开发机），也要把 Tools\pacinjector.exe 写回标准 Host 路径
        if (!string.IsNullOrWhiteSpace(configured) && IsMainToolsStoredPath(configured))
        {
            return new HostExecutableResolution(
                standardExists ? resolvedStandard : null,
                standardStored,
                standardExists
                    ? HostExecutableResolutionSource.Standard
                    : HostExecutableResolutionSource.Missing);
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var resolvedConfigured = ResolvePath(configured, baseDirectory);
            if (resolvedConfigured is not null && File.Exists(resolvedConfigured))
            {
                return new HostExecutableResolution(
                    resolvedConfigured,
                    configured,
                    HostExecutableResolutionSource.Configured);
            }
        }

        if (standardExists)
        {
            // 自定义缺失路径不自动改写；仅 Main Tools→Host 走迁移
            return new HostExecutableResolution(
                resolvedStandard,
                standardStored,
                HostExecutableResolutionSource.Standard);
        }

        return new HostExecutableResolution(
            null,
            string.IsNullOrWhiteSpace(configured) ? standardStored : configured,
            HostExecutableResolutionSource.Missing);
    }

    public static bool IsMainToolsStoredPath(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return false;
        }

        var normalized = storedPath.Trim().Replace('\\', Path.DirectorySeparatorChar);
        var expectedNormalized = AgentsPaths.MainToolsExecutable
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar);

        if (string.Equals(normalized, expectedNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            if (!string.Equals(
                    Path.GetFileName(normalized),
                    AgentsPaths.MainToolsFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var directoryName = Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                return false;
            }

            return string.Equals(
                Path.GetFileName(directoryName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                "Tools",
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string? ResolvePath(string? value, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var trimmed = value.Trim().Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(trimmed))
            {
                return Path.GetFullPath(trimmed);
            }

            return Path.GetFullPath(trimmed, baseDirectory);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeStoredPath(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
