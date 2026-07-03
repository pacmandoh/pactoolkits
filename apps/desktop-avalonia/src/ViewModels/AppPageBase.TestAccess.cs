using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public abstract partial class AppPageBase
{
    internal void TestInjectDbServices(
        IDbConnectionMonitorService? dbMonitor = null,
        IDbAccessGuard? accessGuard = null,
        IAppStartupStateService? startupState = null)
    {
        if (dbMonitor is not null)
        {
            _cachedDbMonitor = dbMonitor;
            HookDbMonitor(dbMonitor);
        }

        if (accessGuard is not null)
        {
            _cachedAccessGuard = accessGuard;
        }

        if (startupState is not null)
        {
            _cachedStartupState = startupState;
        }
    }

    internal Task TestRunReloadAsync(Func<CancellationToken, Task> action)
        => RunReloadAsync(action);

    internal Task TestRunReloadCoreAsync()
        => RunReloadAsync(ReloadCoreAsync);
}
