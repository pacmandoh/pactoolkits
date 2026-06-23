using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Agent.Contracts.Abstractions;

/// <summary>
/// Reserved for desktop-orchestrated agent tasks (queue snapshot, status).
/// Today AHK polls Postgres directly; register an implementation when Desktop owns task dispatch.
/// </summary>
public interface IAgentTaskService
{
    Task<IReadOnlyList<AgentTaskStatusSnapshot>> GetQueueSnapshotAsync(CancellationToken ct);
}

public sealed record AgentTaskStatusSnapshot(long TaskId, AgentTaskStatus Status, string? Summary);
