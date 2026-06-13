namespace PacToolkits.Agent.Contracts.Models;

public enum AgentTaskStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Discarded
}
