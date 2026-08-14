using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Integration.Agents;

/// <summary>
/// Host 启停闸门专用：按模块入口路径匹配结束残留进程
/// 非常驻监管；仅 Host 树收不干净时防止双实例
/// </summary>
internal static class AgentsModuleOrphans
{
    /// <summary>
    /// 结束 catalog 内各模块入口的残留进程，并清理 module.ready 镜像
    /// </summary>
    /// <returns>闸门后已无匹配残留</returns>
    public static bool Clear(
        AgentsOptions options,
        IReadOnlyList<ModuleDescriptor> modules,
        IAppLogger logger)
    {
        if (!OperatingSystem.IsWindows() || modules.Count == 0)
        {
            return true;
        }

        var agentsDir = AgentsDeployPaths.ResolveAgentsDir(options);
        var lingering = false;

        foreach (var module in modules)
        {
            var entry = AgentsDeployPaths.ResolveModuleEntry(options, module.Id);
            if (string.IsNullOrWhiteSpace(entry))
            {
                ClearReady(agentsDir, module.Id);
                continue;
            }

            TerminateEntry(entry, module.Id, logger);
            ClearReady(agentsDir, module.Id);

            if (IsEntryAlive(entry))
            {
                lingering = true;
                logger.Warn(
                    "Agents",
                    "agents.module_orphan.linger",
                    "Module entry process still alive after host-gate sweep",
                    context: new { moduleId = module.Id, entry });
            }
        }

        return !lingering;
    }

    private static bool MatchesExe(Process process, string normalizedEntry, bool permissiveOnAccessDenied)
    {
        try
        {
            if (process.HasExited)
            {
                return false;
            }

            var modulePath = Normalize(process.MainModule?.FileName);
            if (modulePath is null)
            {
                return false;
            }

            return string.Equals(modulePath, normalizedEntry, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return permissiveOnAccessDenied;
        }
    }

    private static void TerminateEntry(string entryPath, string moduleId, IAppLogger logger)
    {
        foreach (var p in Enumerate(entryPath))
        {
            try
            {
                Terminate(p, logger, moduleId);
            }
            finally
            {
                p.Dispose();
            }
        }
    }

    private static bool IsEntryAlive(string entryPath)
    {
        var list = Enumerate(entryPath);
        try
        {
            return list.Count > 0;
        }
        finally
        {
            foreach (var p in list)
            {
                p.Dispose();
            }
        }
    }

    private static List<Process> Enumerate(string entryPath)
    {
        var result = new List<Process>();
        var normalized = Normalize(entryPath);
        if (normalized is null || !File.Exists(normalized))
        {
            return result;
        }

        var processName = Path.GetFileNameWithoutExtension(normalized);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return result;
        }

        foreach (var p in Process.GetProcessesByName(processName))
        {
            if (MatchesExe(p, normalized, permissiveOnAccessDenied: true))
            {
                result.Add(p);
            }
            else
            {
                p.Dispose();
            }
        }

        return result;
    }

    private static void Terminate(Process p, IAppLogger logger, string moduleId)
    {
        if (p.HasExited)
        {
            return;
        }

        try
        {
            if (p.CloseMainWindow() && p.WaitForExit(2200))
            {
                logger.Info(
                    "Agents",
                    "agents.module_orphan.closed",
                    "Closed orphan module window at host gate",
                    new { moduleId, pid = p.Id });
                return;
            }
        }
        catch (Exception ex)
        {
            logger.Warn(
                "Agents",
                "agents.module_orphan.close_fail",
                "Failed to close orphan module window",
                ex,
                new { moduleId, pid = p.Id });
        }

        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(2500);
                logger.Info(
                    "Agents",
                    "agents.module_orphan.killed",
                    "Killed orphan module process at host gate",
                    new { moduleId, pid = p.Id });
            }
        }
        catch (Exception ex)
        {
            logger.Warn(
                "Agents",
                "agents.module_orphan.kill_fail",
                "Failed to kill orphan module process",
                ex,
                new { moduleId, pid = p.Id });
        }
    }

    private static void ClearReady(string? agentsDir, string moduleId)
    {
        if (string.IsNullOrWhiteSpace(agentsDir))
        {
            return;
        }

        try
        {
            var path = AgentsPaths.ModuleReadyPath(agentsDir, moduleId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ready 残留不阻断闸门
        }
    }

    private static string? Normalize(string? path)
        => AgentsPath.ResolvePath(path, AppContext.BaseDirectory);
}
