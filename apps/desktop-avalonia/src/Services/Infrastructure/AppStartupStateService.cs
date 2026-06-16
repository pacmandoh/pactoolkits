using System;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public interface IAppStartupStateService
{
    bool IsDbInitCompleted { get; }
    event Action? DbInitCompleted;
    void MarkDbInitCompleted();
}

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

