using PacToolkits.Agent.Contracts.Commands;
using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Agent.Contracts.Events;

public sealed record AgentExecutionEvent(
    DateTimeOffset AtUtc,
    AgentCommandKind? Command,
    ToolRunState? RuntimeState,
    AgentTaskStatus? TaskStatus,
    string Message,
    string? Detail = null);
