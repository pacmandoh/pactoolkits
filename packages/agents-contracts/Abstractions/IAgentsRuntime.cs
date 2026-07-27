using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Abstractions;

/// <summary>
/// 单个 Agents 运行时：常驻 Host + 可热插拔 Modules 的启停与状态
/// </summary>
public interface IAgentsRuntime : IDisposable
{
    event Action? StatusChanged;

    AgentsDescriptor Descriptor { get; }

    /// <summary>缓存的模块清单（Reload / poll reconcile；发现 ≠ 自动挂载）</summary>
    IReadOnlyList<ModuleDescriptor> Modules { get; }

    string MinDbSchema { get; }

    string MaxDbSchema { get; }

    AgentsRunState HostState { get; }

    bool IsHostRunning { get; }

    DateTimeOffset? HostLastLaunchAt { get; }

    string? HostLastError { get; }

    string HostVersion { get; }

    void Reload();

    /// <summary>
    /// 按模块 id 取运行态（入口进程 + module.ready；不读 Enabled）
    /// </summary>
    AgentsRunState GetModuleState(string moduleId);

    /// <summary>
    /// 模块是否允许 Start / 自动挂载（读 <c>Agents.Modules[id].Enabled</c>）
    /// </summary>
    bool IsModuleEnabled(string moduleId);

    /// <summary>模块最近一次成功挂载时间（本会话）</summary>
    DateTimeOffset? GetModuleLastLaunchAt(string moduleId);

    /// <summary>模块最近一次启停错误（无则 null）</summary>
    string? GetModuleLastError(string moduleId);

    /// <summary>读 Modules/&lt;id&gt;/module.json 的 version；缺失为「未配置」/「未知」</summary>
    string GetModuleVersion(string moduleId);

    Task<AgentsCommandResult> StartOrRestartAsync(CancellationToken ct = default);

    Task<AgentsCommandResult> StopAsync(CancellationToken ct = default);

    /// <summary>
    /// 启动或重挂指定模块；Host 已在运行时保持不杀进程
    /// </summary>
    Task<AgentsCommandResult> StartModuleAsync(string moduleId, CancellationToken ct = default);

    /// <summary>
    /// 硬停指定模块进程；不退出 Host
    /// </summary>
    Task<AgentsCommandResult> StopModuleAsync(string moduleId, CancellationToken ct = default);
}
