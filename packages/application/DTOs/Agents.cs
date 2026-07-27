namespace PacToolkits.Application.DTOs;

/// <summary>
/// Desktop 侧 Agents 配置根（可执行路径 + 模块开关）
/// </summary>
public sealed class AgentsConfigDto
{
    /// <summary>须与 agents-contracts 中 <c>AgentsPaths.HostExecutable</c> 保持一致</summary>
    public const string DefaultHostExecutable = @".\Agents\Agents.exe";

    public string ExecutablePath { get; set; } = DefaultHostExecutable;

    public string ProcessName { get; set; } = string.Empty;

    public Dictionary<string, ModuleOptionsDto> Modules { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 模块级开关
/// </summary>
public sealed class ModuleOptionsDto
{
    public bool Enabled { get; set; } = true;
}
