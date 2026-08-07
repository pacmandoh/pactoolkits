using System;
using System.Diagnostics;
using System.IO;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

/// <summary>
/// Host/模块路径与版本读取（文件 I/O，无运行时状态）
/// </summary>
internal static class AgentsDeployPaths
{
    public static string? ResolveExe(string? path)
        => AgentsPath.ResolvePath(path, AppContext.BaseDirectory);

    public static string? ResolveAgentsDir(AgentsOptions options)
    {
        // 与配置规范化共用 Host 路径解析，确保模块目录来源一致
        var resolution = AgentsPath.ResolveHost(options.ExecutablePath, AppContext.BaseDirectory);
        if (resolution.ResolvedPath is null)
        {
            return null;
        }

        var agentsDir = Path.GetDirectoryName(resolution.ResolvedPath);
        return string.IsNullOrWhiteSpace(agentsDir) ? null : agentsDir;
    }

    public static string? ResolveModuleEntry(AgentsOptions options, string moduleId)
    {
        var agentsDir = ResolveAgentsDir(options);
        return agentsDir is null
            ? null
            : AgentsPath.TryResolveModuleEntryPath(agentsDir, moduleId);
    }

    public static string BuildConfigArguments(string configPath)
    {
        static string Q(string value)
            => $"\"{(value ?? string.Empty).Replace("\"", "\\\"")}\"";

        return $"--config {Q(configPath ?? string.Empty)}";
    }

    public static string ReadHostVersion(string executablePath)
    {
        var resolvedPath = ResolveExe(executablePath);
        if (resolvedPath is null || !File.Exists(resolvedPath))
        {
            return "未配置";
        }

        var agentsDir = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(agentsDir))
        {
            var fromManifest = AgentsPath.TryReadHostVersion(
                Path.Combine(agentsDir, "ReleaseManifest.json"));
            if (!string.IsNullOrWhiteSpace(fromManifest))
            {
                return fromManifest;
            }
        }

        return ReadFileVersion(resolvedPath);
    }

    public static string ReadModuleVersion(string agentsDir, string moduleId)
    {
        var manifestPath = AgentsPaths.ModuleManifestPath(agentsDir, moduleId);
        if (!File.Exists(manifestPath))
        {
            return "未配置";
        }

        return AgentsPath.TryReadModuleVersion(manifestPath) ?? "未知";
    }

    private static string ReadFileVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return string.IsNullOrWhiteSpace(info.FileVersion) ? "未知" : info.FileVersion;
        }
        catch
        {
            return "未知";
        }
    }
}
