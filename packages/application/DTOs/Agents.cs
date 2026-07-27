namespace PacToolkits.Application.DTOs;

/// <summary>
/// Application 层使用的 Agents Host 配置和模块启用状态
/// </summary>
public sealed class AgentsConfigDto
{
    public const string DefaultHostExecutable = @".\Agents\Agents.exe";

    public string ExecutablePath { get; set; } = DefaultHostExecutable;

    public string ProcessName { get; set; } = string.Empty;

    public Dictionary<string, ModuleOptionsDto> Modules { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Application 层的模块启用状态
/// </summary>
public sealed class ModuleOptionsDto
{
    public bool Enabled { get; set; } = true;
}
