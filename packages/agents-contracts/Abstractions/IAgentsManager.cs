using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Abstractions;

/// <summary>
/// 已注册 Agents 运行时的聚合入口（按 Id 取实例、同步配置、统一停止）
/// </summary>
public interface IAgentsManager
{
    IAgentsRuntime GetRequired(string id);

    Task<IReadOnlyDictionary<string, AgentsCommandResult>> SyncConfigAsync(
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, AgentsCommandResult>> StopAllAsync(
        CancellationToken ct = default);
}
