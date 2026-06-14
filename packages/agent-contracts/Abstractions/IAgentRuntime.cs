using PacToolkits.Agent.Contracts.Agents;
using PacToolkits.Agent.Contracts.Commands;

namespace PacToolkits.Agent.Contracts.Abstractions;

public interface IAgentRuntime : IDisposable
{
    event Action? StatusChanged;

    AgentDescriptor Descriptor { get; }

    string ExecutablePath { get; }

    ToolRunState State { get; }

    bool IsRunning { get; }

    DateTimeOffset? LastLaunchAt { get; }

    string? LastError { get; }

    string ToolVersion { get; }

    void Reload();

    Task<ToolCommandResult> StartOrRestartAsync(CancellationToken ct = default);

    Task<ToolCommandResult> StopAsync(CancellationToken ct = default);
}
