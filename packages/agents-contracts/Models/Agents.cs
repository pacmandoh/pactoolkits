using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Contracts.Models;

/// <summary>
/// Desktop / Host 共享的 Agents 配置根（可执行路径 + 模块开关）
/// </summary>
public sealed class AgentsOptions
{
    public string ExecutablePath { get; set; } = AgentsPaths.HostExecutable;

    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// 按模块 id 的开关（与磁盘 Modules/ 对齐）；业务参数在 {ConfigDir}/agents/modules/&lt;Id&gt;/settings.json
    /// </summary>
    public Dictionary<string, ModuleOptions> Modules { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 模块级开关（热插拔控制面；观测不读 Enabled）
/// </summary>
public sealed class ModuleOptions
{
    public bool Enabled { get; set; } = true;
}
