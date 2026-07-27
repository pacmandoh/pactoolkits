using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Contracts.Models;

/// <summary>
/// Desktop 持久化的 Agents Host 配置和模块启用状态
/// </summary>
public sealed class AgentsOptions
{
    public string ExecutablePath { get; set; } = AgentsPaths.HostExecutable;

    public string ProcessName { get; set; } = string.Empty;

    public Dictionary<string, ModuleOptions> Modules { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 模块启动授权；运行状态由进程观测独立计算
/// </summary>
public sealed class ModuleOptions
{
    public bool Enabled { get; set; } = true;
}
