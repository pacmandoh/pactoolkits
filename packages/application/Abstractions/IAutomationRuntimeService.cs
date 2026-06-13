using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

public interface IAutomationRuntimeService : IDisposable
{
    event Action? StatusChanged;

    AutomationAhkOptionsDto CurrentOptions { get; }

    AutomationRunState State { get; }

    bool IsRunning { get; }

    DateTimeOffset? LastLaunchAt { get; }

    string? LastError { get; }

    string ToolVersion { get; }

    void Reload();

    Task SaveOptionsAsync(AutomationAhkOptionsDto options, CancellationToken ct = default);

    Task<AutomationCommandResult> StartOrRestartAsync(CancellationToken ct = default);

    Task<AutomationCommandResult> StopAsync(CancellationToken ct = default);
}
