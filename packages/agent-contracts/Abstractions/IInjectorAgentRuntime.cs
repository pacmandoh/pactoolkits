using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Agent.Contracts.Abstractions;

public interface IInjectorAgentRuntime : IAgentRuntime
{
    AgentRuntimeConfig RuntimeConfig { get; }

    AhkToolOptions GetAhkToolOptions();

    Task SaveOptionsAsync(AhkToolOptions options, CancellationToken ct = default);
}
