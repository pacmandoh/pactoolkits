using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Abstractions;

/// <summary>
/// 单个 Agents 运行时：常驻 Host + 可装卸 Injector 模块的启停与状态
/// </summary>
public interface IAgentsRuntime : IDisposable
{
    event Action? StatusChanged;

    AgentsDescriptor Descriptor { get; }

    bool IsInjectorEnabled { get; }

    string MinDbSchema { get; }

    string MaxDbSchema { get; }

    AgentsRunState HostState { get; }

    AgentsRunState InjectorState { get; }

    bool IsHostRunning { get; }

    bool IsInjectorRunning { get; }

    DateTimeOffset? HostLastLaunchAt { get; }

    DateTimeOffset? InjectorLastLaunchAt { get; }

    string? HostLastError { get; }

    string? InjectorLastError { get; }

    string HostVersion { get; }

    string InjectorVersion { get; }

    void Reload();

    Task<AgentsCommandResult> StartOrRestartAsync(CancellationToken ct = default);

    Task<AgentsCommandResult> StopAsync(CancellationToken ct = default);

    /// <summary>
    /// 启动或重挂 Injector；Host 已在运行时保持不杀进程
    /// </summary>
    Task<AgentsCommandResult> StartInjectorAsync(CancellationToken ct = default);

    /// <summary>
    /// 硬停 Injector 模块进程；在 Host 支持常驻时不退出 Host
    /// </summary>
    Task<AgentsCommandResult> StopInjectorAsync(CancellationToken ct = default);
}
