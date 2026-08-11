using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Runtime;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public abstract partial class AppPageBase
{
    internal void TestInjectDbServices(
        IDbConnectionMonitorService? dbMonitor = null,
        IDbAccessGuard? accessGuard = null,
        IAppStartupStateService? startupState = null,
        TimeProvider? timeProvider = null)
    {
        if (dbMonitor is not null)
        {
            _cachedDbMonitor = dbMonitor;
            if (RequiresLocalDbForReload)
            {
                HookDbMonitor(dbMonitor);
            }
        }

        if (accessGuard is not null)
        {
            _cachedAccessGuard = accessGuard;
        }

        if (startupState is not null)
        {
            _cachedStartupState = startupState;
        }

        if (timeProvider is not null)
        {
            _cachedTime = timeProvider;
        }
    }

    internal void TestSetServiceRetryDelay(TimeSpan delay)
        => _serviceRetryDelay = delay;

    internal DateTimeOffset? TestNextRetryAt()
    {
        lock (_serviceRetryGate)
        {
            return _nextRetryAt;
        }
    }

    internal Task TestRunReloadAsync(Func<CancellationToken, Task> action)
        => RunReloadAsync(action);

    internal Task TestRunReloadCoreAsync()
        => RunReloadAsync(ReloadCoreAsync);

    internal Task TestOnPageActivatedAsync()
        => OnPageActivatedAsync();

    internal bool TestIsLookupCatalogSuspended()
        => IsLookupCatalogSuspended();
}
