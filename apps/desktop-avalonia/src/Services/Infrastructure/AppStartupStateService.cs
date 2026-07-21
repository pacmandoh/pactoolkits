using System;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>应用启动阶段状态入口</summary>
public interface IAppStartupStateService
{
    bool IsDbInitCompleted { get; }
    event Action? DbInitCompleted;
    void MarkDbInitCompleted();
}

/// <summary>跟踪应用启动阶段（首屏就绪等）</summary>
public sealed class AppStartupStateService : IAppStartupStateService
{
    private bool _isDbInitCompleted;
    public bool IsDbInitCompleted => _isDbInitCompleted;
    public event Action? DbInitCompleted;

    public void MarkDbInitCompleted()
    {
        if (_isDbInitCompleted)
        {
            return;
        }

        _isDbInitCompleted = true;
        DbInitCompleted?.Invoke();
    }
}

