using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Abstractions;

/// <summary>
/// 已注册的 Agents 运行时：查找实例、同步配置、统一停止
/// </summary>
public interface IAgentsManager
{
    IAgentsRuntime GetRequired(string id);

    Task<IReadOnlyDictionary<string, AgentsCommandResult>> SyncConfigAsync(
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, AgentsCommandResult>> StopAllAsync(
        CancellationToken ct = default);
}
