using PacToolkits.Agents.Contracts.Commands;

namespace PacToolkits.Agents.Contracts.Abstractions;

public interface IAgentsManager
{
    IAgentsRuntime GetRequired(string id);

    Task<IReadOnlyDictionary<string, AgentsCommandResult>> SyncConfigAsync(
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, AgentsCommandResult>> StopAllAsync(
        CancellationToken ct = default);
}
