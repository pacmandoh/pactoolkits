using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Agent.Contracts.Abstractions;

public interface IAgentConfigWriter
{
    AutomationToolsOptions LoadAutomationTools();

    Task WriteAutomationToolsAsync(AutomationToolsOptions options, CancellationToken ct);
}
