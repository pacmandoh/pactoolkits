using PacToolkits.Agent.Contracts.Agents;

namespace PacToolkits.Agent.Contracts.Abstractions;

public interface IAgentRegistry
{
    IReadOnlyCollection<AgentDescriptor> Descriptors { get; }

    IAgentRuntime GetRequired(string agentId);

    bool TryGet(string agentId, out IAgentRuntime? runtime);
}
