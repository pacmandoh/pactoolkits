using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Abstractions;

/// <summary>
/// 聚合已注册的 Agents 运行时，提供实例查找、配置同步和统一停止契约
/// </summary>
public interface IAgentsManager
{
    IAgentsRuntime GetRequired(string id);

    Task<IReadOnlyDictionary<string, AgentsCommandResult>> SyncConfigAsync(
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, AgentsCommandResult>> StopAllAsync(
        CancellationToken ct = default);
}
