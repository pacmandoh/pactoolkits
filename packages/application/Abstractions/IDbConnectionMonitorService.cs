using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

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
