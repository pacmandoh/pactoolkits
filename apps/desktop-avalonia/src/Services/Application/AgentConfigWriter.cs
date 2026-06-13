using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Models;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public sealed class AgentConfigWriter : IAgentConfigWriter
{
    private readonly IAutomationToolsConfigService _automationConfig;

    public AgentConfigWriter(IAutomationToolsConfigService automationConfig)
    {
        _automationConfig = automationConfig;
    }

    public AutomationToolsOptions LoadAutomationTools()
        => _automationConfig.Load();

    public Task WriteAutomationToolsAsync(AutomationToolsOptions options, CancellationToken ct)
        => _automationConfig.SaveAsync(options, ct);
}
