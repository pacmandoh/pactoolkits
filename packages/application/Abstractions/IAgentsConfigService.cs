using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IAgentsConfigService
{
    AgentsConfigDto Load();

    Task SaveAsync(AgentsConfigDto options, CancellationToken ct);

    Task SetInjectorEnabledAsync(bool enabled, CancellationToken ct);
}
