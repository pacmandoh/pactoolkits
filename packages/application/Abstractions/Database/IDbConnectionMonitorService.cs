using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 连接存活监控与断连/重连信号；ProbeAsync 供健康检查
/// </summary>
public interface IDbConnectionMonitorService : IDisposable
{
    bool IsConnected { get; }

    event Action? Disconnected;
    event Action? Reconnected;
    event Action<string>? ConnectionFailed;

    void Start();
    void Signal();

    Task<DbProbeReport> ProbeAsync(DbProbeKind kind, CancellationToken ct);
}
