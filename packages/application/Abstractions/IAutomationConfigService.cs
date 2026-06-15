using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IAutomationConfigService
{
    AutomationConfigDto Load();

    Task SaveAsync(AutomationConfigDto options, CancellationToken ct);

    Task SetAgentEnabledAsync(string agentId, bool enabled, CancellationToken ct);
}
