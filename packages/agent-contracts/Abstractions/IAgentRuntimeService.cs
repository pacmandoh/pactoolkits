using PacToolkits.Agent.Contracts.Commands;
using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Agent.Contracts.Abstractions;

public interface IAgentRuntimeService : IDisposable
{
    event Action? StatusChanged;

    AhkToolOptions CurrentOptions { get; }

    AgentRuntimeConfig RuntimeConfig { get; }

    ToolRunState State { get; }

    bool IsRunning { get; }

    DateTimeOffset? LastLaunchAt { get; }

    string? LastError { get; }

    string ToolVersion { get; }

    void Reload();

    Task SaveOptionsAsync(AhkToolOptions options, CancellationToken ct = default);

    Task<ToolCommandResult> StartOrRestartAsync(CancellationToken ct = default);

    Task<ToolCommandResult> StopAsync(CancellationToken ct = default);
}
