using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Agent.Contracts.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

/// <summary>
/// Placeholder for future agent-side task orchestration. AHK still polls Postgres directly today.
/// </summary>
public sealed class AgentTaskService : IAgentTaskService
{
    public Task<IReadOnlyList<AgentTaskStatusSnapshot>> GetQueueSnapshotAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AgentTaskStatusSnapshot>>(Array.Empty<AgentTaskStatusSnapshot>());
}
