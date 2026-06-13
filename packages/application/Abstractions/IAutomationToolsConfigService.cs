using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Application.Abstractions;

public interface IAutomationToolsConfigService
{
    AutomationToolsOptions Load();

    Task SaveAsync(AutomationToolsOptions options, CancellationToken ct);
}
