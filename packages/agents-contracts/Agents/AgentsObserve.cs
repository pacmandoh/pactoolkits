namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Host 监督事实 → RunState（纯函数，无 I/O）
///
/// 仅 Host 写 Snapshot 时调用；Desktop 消费 <see cref="AgentsStatusModule.State"/>，不再二次推导
/// </summary>
public static class AgentsObserve
{
    /// <param name="processAlive">Host 进程仍在（Host 发布 status 时恒真）</param>
    /// <param name="launching">Desktop 冷启动 Host、尚无首帧 Snapshot</param>
    /// <param name="stickyFailed">Desktop 侧 CreateProcess/门禁失败、Host 未起来</param>
    public static AgentsRunState Host(
        bool processAlive,
        bool launching,
        bool stickyFailed)
    {
        if (processAlive)
        {
            return AgentsRunState.Running;
        }

        if (launching)
        {
            return AgentsRunState.Starting;
        }

        if (stickyFailed)
        {
            return AgentsRunState.Failed;
        }

        return AgentsRunState.Stopped;
    }

    /// <summary>
    /// 模块语义态：仅由 desired + 进程/ready + 启动失败 合成
    /// </summary>
    public static AgentsRunState Module(
        bool desired,
        bool processAlive,
        bool ready,
        bool startFailed,
        string? lastError)
    {
        if (!desired)
        {
            return AgentsRunState.Stopped;
        }

        if (processAlive && ready)
        {
            return AgentsRunState.Running;
        }

        if (startFailed || (!string.IsNullOrWhiteSpace(lastError) && !processAlive))
        {
            return AgentsRunState.Failed;
        }

        return AgentsRunState.Starting;
    }

    public static bool TryParseState(string? raw, out AgentsRunState state)
        => Enum.TryParse(raw, ignoreCase: true, out state);
}
