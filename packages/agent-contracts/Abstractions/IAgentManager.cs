namespace PacToolkits.Agent.Contracts.Abstractions;

using PacToolkits.Agent.Contracts.Agents;

public interface IAgentManager : IAgentRegistry
{
    IAgentRuntime Get(string agentId) => GetRequired(agentId);

    IAgentRuntime Get(AgentId agentId) => GetRequired(agentId.Value);
}
