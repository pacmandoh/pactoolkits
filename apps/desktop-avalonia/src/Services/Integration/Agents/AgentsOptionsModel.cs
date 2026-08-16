using System;
using System.Linq;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;

namespace PacToolkits.Desktop.Avalonia.Services.Integration.Agents;

/// <summary>
/// Agents 配置项规范化与比较（无 I/O）
/// </summary>
internal static class AgentsOptionsModel
{
    public static AgentsOptions Normalize(AgentsOptions? src)
    {
        var opt = src ?? new AgentsOptions();
        return new AgentsOptions
        {
            ExecutablePath = opt.ExecutablePath.Trim(),
            ProcessName = opt.ProcessName.Trim(),
            Modules = opt.Modules.ToDictionary(
                static pair => pair.Key,
                static pair => new ModuleOptions { Enabled = pair.Value.Enabled },
                StringComparer.Ordinal),
        };
    }

    public static AgentsOptions Clone(AgentsOptions src) => Normalize(src);

    public static bool Same(AgentsOptions a, AgentsOptions b)
    {
        if (!string.Equals(a.ExecutablePath, b.ExecutablePath, StringComparison.Ordinal)
            || !string.Equals(a.ProcessName, b.ProcessName, StringComparison.Ordinal)
            || a.Modules.Count != b.Modules.Count)
        {
            return false;
        }

        foreach (var (id, left) in a.Modules)
        {
            if (!b.Modules.TryGetValue(id, out var right) || left.Enabled != right.Enabled)
            {
                return false;
            }
        }

        return true;
    }

    public static AgentsOptions FromResolution(
        AgentsOptions agents,
        HostExecutableResolution resolution)
    {
        var storedPath = string.IsNullOrWhiteSpace(resolution.StoredPath)
            ? AgentsPaths.HostExecutable
            : resolution.StoredPath.Trim();

        var processName = agents.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            processName = AgentsPaths.HostProcessName;
        }

        return Normalize(new AgentsOptions
        {
            ExecutablePath = storedPath,
            ProcessName = processName ?? string.Empty,
            Modules = agents.Modules,
        });
    }
}
