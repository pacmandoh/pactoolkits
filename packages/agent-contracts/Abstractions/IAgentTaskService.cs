using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Agent.Contracts.Abstractions;

/// <summary>
/// Reserved boundary for future agent-side task orchestration.
/// Current AHK runtime still polls database directly.
/// </summary>
public interface IAgentTaskService
{
    Task<IReadOnlyList<AgentTaskStatusSnapshot>> GetQueueSnapshotAsync(CancellationToken ct);
}

public sealed record AgentTaskStatusSnapshot(long TaskId, AgentTaskStatus Status, string? Summary);
