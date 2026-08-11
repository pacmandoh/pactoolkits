using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 提供 Desktop Agents 全局配置和模块启用状态的持久化操作
/// </summary>
public interface IAgentsConfigService
{
    AgentsConfigDto Load();

    Task SaveAsync(AgentsConfigDto options, CancellationToken ct);

    Task SetModuleEnabledAsync(string moduleId, bool enabled, CancellationToken ct);
}
