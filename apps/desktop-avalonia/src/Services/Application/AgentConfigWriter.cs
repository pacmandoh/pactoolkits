using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Models;
using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agent;

public sealed class AgentConfigWriter : IAgentConfigWriter
{
    private readonly IAutomationConfigService _automationConfig;

    public AgentConfigWriter(IAutomationConfigService automationConfig)
    {
        _automationConfig = automationConfig;
    }

    public AutomationToolsOptions LoadAutomationTools()
        => AutomationContractMapper.ToContract(_automationConfig.Load());

    public Task WriteAutomationToolsAsync(AutomationToolsOptions options, CancellationToken ct)
        => _automationConfig.SaveAsync(AutomationContractMapper.ToApplication(options), ct);
}
