namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// 表示 Host 或模块的运行状态；<see cref="AgentsRunState.Starting"/> 用于区分进程已创建但尚未就绪的阶段
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
/// 提供 <see cref="AgentsRunState"/> 的状态分类操作
/// </summary>
public static class AgentsRunStateExtensions
{
    public static bool IsActive(this AgentsRunState state)
        => state is AgentsRunState.Running or AgentsRunState.Starting;
}
