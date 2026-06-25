namespace PacToolkits.Agent.Contracts.Abstractions;

public interface IAgentRegistry
{
    IAgentRuntime GetRequired(string agentId);
}
