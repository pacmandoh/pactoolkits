namespace PacToolkits.Agent.Contracts.Commands;

public enum AgentCommandKind
{
    Start,
    Stop,
    Restart
}

public sealed record AgentCommand(AgentCommandKind Kind, string? Payload = null);
