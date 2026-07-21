namespace PacToolkits.Agents.Contracts.Agents;

public enum AgentsRunState
{
    Stopped,
    Starting,
    Running,
    Failed,
    Unknown
}

public static class AgentsRunStateExtensions
{
    public static bool IsActive(this AgentsRunState state)
        => state is AgentsRunState.Running or AgentsRunState.Starting;
}
