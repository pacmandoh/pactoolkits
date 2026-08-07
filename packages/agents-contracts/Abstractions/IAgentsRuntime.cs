using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Abstractions;

/// <summary>
/// Desktop 侧单个 Host 入口：OS 启停 Host、会话 desired、UI 投影
///
/// 链路（管道）由实现内部组合 <see cref="IAgentsClient"/>，不在本契约暴露
/// 模块运维与 catalog 真相在 Host Snapshot
/// </summary>
public interface IAgentsRuntime : IDisposable
{
    /// <summary>UI 刷新（含 Snapshot 与 Host 生命周期）</summary>
    event Action? StatusChanged;

    AgentsDescriptor Descriptor { get; }

    IReadOnlyList<ModuleDescriptor> Modules { get; }

    string MinDesktop { get; }

    string MaxDesktop { get; }

    AgentsRunState HostState { get; }

    bool IsHostRunning { get; }

    DateTimeOffset? HostLastLaunchAt { get; }

    string? HostLastError { get; }

    string HostVersion { get; }

    void Reload();

    /// <summary>
    /// 根据 Host 发布状态与会话意图获取模块运行状态（不读启用位）
    /// </summary>
    AgentsRunState GetModuleState(string moduleId);

    /// <summary>
    /// 获取模块是否获准启动或在 Host 启动后自动挂载
    /// </summary>
    bool IsModuleEnabled(string moduleId);

    /// <summary>获取当前 Desktop 会话中模块最近一次成功启动的时间</summary>
    DateTimeOffset? GetModuleLastLaunchAt(string moduleId);

    /// <summary>获取模块最近一次生命周期操作错误；没有错误时返回 <see langword="null"/></summary>
    string? GetModuleLastError(string moduleId);

    /// <summary>获取模块描述文件声明的版本；无法解析时返回对应的状态文本</summary>
    string GetModuleVersion(string moduleId);

    Task<AgentsCommandResult> StartOrRestartAsync(CancellationToken ct = default);

    Task<AgentsCommandResult> StopAsync(CancellationToken ct = default);

    /// <summary>
    /// 启动指定模块；模块已经运行时仅重启该模块并保持 Host 进程不变
    /// </summary>
    Task<AgentsCommandResult> StartModuleAsync(string moduleId, CancellationToken ct = default);

    /// <summary>
    /// 停止指定模块进程并保持 Host 进程不变
    /// </summary>
    Task<AgentsCommandResult> StopModuleAsync(string moduleId, CancellationToken ct = default);
}
