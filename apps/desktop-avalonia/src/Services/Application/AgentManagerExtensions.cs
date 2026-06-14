using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Agents;
using PacToolkits.Agent.Contracts.Commands;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class AgentManagerExtensions
{
    public static IAgentRuntime GetAgent(this IAgentManager manager, AgentId agentId)
        => manager.GetRequired(agentId.Value);

    public static IAgentRuntime GetAgent(this IAgentManager manager, string agentId)
        => manager.GetRequired(agentId);

    public static AutomationCommandResult ToApplication(this ToolCommandResult result)
        => new(result.Ok, result.Message, result.SuppressToast);

    public static AutomationRunState GetAutomationState(this IAgentManager manager, string agentId)
        => AutomationContractMapper.ToApplication(manager.GetRequired(agentId).State);
}
