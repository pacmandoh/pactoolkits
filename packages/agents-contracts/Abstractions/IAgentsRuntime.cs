using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Abstractions;

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

    /// <summary>Start or remount Injector; keeps Host up when it is already running.</summary>
    Task<AgentsCommandResult> StartInjectorAsync(CancellationToken ct = default);

    /// <summary>Hard-stop Injector module process; Host stays resident when supported.</summary>
    Task<AgentsCommandResult> StopInjectorAsync(CancellationToken ct = default);
}
