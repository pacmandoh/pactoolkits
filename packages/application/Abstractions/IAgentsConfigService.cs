using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// Agents 配置的加载/保存与 Injector 启停开关
/// </summary>
public interface IAgentsConfigService
{
    AgentsConfigDto Load();

    Task SaveAsync(AgentsConfigDto options, CancellationToken ct);

    Task SetInjectorEnabledAsync(bool enabled, CancellationToken ct);
}
