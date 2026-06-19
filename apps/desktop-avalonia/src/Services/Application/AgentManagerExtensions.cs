using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Commands;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class AgentManagerExtensions
{
    public static AutomationRunState GetAutomationState(this IAgentRuntime runtime)
        => AutomationContractMapper.ToApplication(runtime.State);

    public static AutomationCommandResult ToApplication(this ToolCommandResult result)
        => new(result.Ok, result.Message, result.SuppressToast);
}
