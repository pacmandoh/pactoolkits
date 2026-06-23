
using PacToolkits.Agent.Contracts.Agents;
using PacToolkits.Agent.Contracts.Commands;

namespace PacToolkits.Agent.Contracts.Abstractions;

public interface IAgentManager : IAgentRegistry
{
    IAgentRuntime Get(string agentId) => GetRequired(agentId);

    IAgentRuntime Get(AgentId agentId) => GetRequired(agentId.Value);

    Task<IReadOnlyDictionary<string, ToolCommandResult>> SyncConfigAsync(
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, ToolCommandResult>> StopAllAsync(
        CancellationToken ct = default);
}
