namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Host / Injector 运行态（含 Starting，便于 UI 区分“进程在但未就绪”）
/// </summary>
public enum AgentsRunState
{
    Stopped,
    Starting,
    Running,
    Failed,
    Unknown
}

/// <summary>
/// AgentsRunState 便捷判定
/// </summary>
public static class AgentsRunStateExtensions
{
    public static bool IsActive(this AgentsRunState state)
        => state is AgentsRunState.Running or AgentsRunState.Starting;
}
