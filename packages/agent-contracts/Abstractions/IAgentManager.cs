
using PacToolkits.Agent.Contracts.Commands;

namespace PacToolkits.Agent.Contracts.Abstractions;

public interface IAgentManager : IAgentRegistry
{
    Task<IReadOnlyDictionary<string, ToolCommandResult>> SyncConfigAsync(
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, ToolCommandResult>> StopAllAsync(
        CancellationToken ct = default);
}
