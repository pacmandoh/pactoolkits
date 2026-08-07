namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// 入口二进制指纹（长度 + mtime），供 Host 模块热更与 Desktop Host 热重载共用
/// </summary>
public readonly record struct AgentsBinaryStamp(long Length, long LastWriteUtcTicks);

/// <summary>
/// 双次稳定观测：活跃时连续两次同指纹才报告变更，避免写半成品文件
/// </summary>
public sealed class AgentsBinaryChange
{
    private AgentsBinaryStamp? _accepted;
    private AgentsBinaryStamp? _candidate;
    private int _candidateObservations;

    public bool Observe(AgentsBinaryStamp? stamp, bool active, out AgentsBinaryStamp changed)
    {
        changed = default;
        if (stamp is null)
        {
            _candidate = null;
            _candidateObservations = 0;
            return false;
        }

        if (_accepted is null || !active)
        {
            _accepted = stamp;
            _candidate = null;
            _candidateObservations = 0;
            return false;
        }

        if (_accepted == stamp)
        {
            _candidate = null;
            _candidateObservations = 0;
            return false;
        }

        if (_candidate != stamp)
        {
            _candidate = stamp;
            _candidateObservations = 1;
            return false;
        }

        _candidateObservations++;
        if (_candidateObservations < 2)
        {
            return false;
        }

        changed = stamp.Value;
        return true;
    }

    public bool Accept(AgentsBinaryStamp stamp)
    {
        if (_candidate != stamp)
        {
            return false;
        }

        _accepted = stamp;
        _candidate = null;
        _candidateObservations = 0;
        return true;
    }
}

/// <summary>
/// 读入口文件指纹；缺失返回 null
/// </summary>
public static class AgentsBinaryStampIO
{
    public static AgentsBinaryStamp? TryRead(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(path);
            return file.Exists
                ? new AgentsBinaryStamp(file.Length, file.LastWriteTimeUtc.Ticks)
                : null;
        }
        catch
        {
            return null;
        }
    }
}
