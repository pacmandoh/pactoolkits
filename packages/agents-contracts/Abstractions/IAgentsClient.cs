using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Contracts.Abstractions;

/// <summary>
/// Desktop ↔ Host 链路：Connect / Send / Snapshot（+ ModuleFailed 同义推送）
///
/// 不监管模块进程、不扫 discovery、不写 module.ready
/// </summary>
public interface IAgentsClient : IDisposable
{
    /// <summary>收到 Host StatusSnapshot 时触发</summary>
    event Action? SnapshotChanged;

    /// <summary>模块 Failed（与 Snapshot.state 同义加强，非第二真相源）</summary>
    event Action<string, string?>? ModuleFailed;

    bool IsLinkConnected { get; }

    /// <summary>最近一帧 StatusSnapshot（管道缓存）</summary>
    AgentsStatus? LastSnapshot { get; }

    /// <summary>管道缓存快照；超过 maxAge 返回 null</summary>
    AgentsStatus? TryGetStatus(TimeSpan maxAge);

    Task<bool> ConnectAsync(string agentsDir, TimeSpan timeout, CancellationToken ct = default);

    void Disconnect();

    bool TrySendDesired(IEnumerable<string> modules);

    bool TrySendQuit();
}
