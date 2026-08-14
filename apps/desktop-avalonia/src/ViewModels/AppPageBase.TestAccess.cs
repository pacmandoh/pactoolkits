using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public abstract partial class AppPageBase
{
    internal void TestInjectServices(
        TimeProvider? timeProvider = null,
        IApiAvailabilityService? apiAvailability = null)
    {
        if (timeProvider is not null)
        {
            _cachedTime = timeProvider;
        }

        if (apiAvailability is not null)
        {
            _cachedApiAvailability = apiAvailability;
            _apiSnap = apiAvailability.Current;
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

    internal bool TestCanToastError(Exception ex) => CanToastError(ex);

    internal IDisposable TestBeginSilentReload() => BeginSilentReload();

    internal bool TestIsLookupCatalogSuspended()
        => IsLookupCatalogSuspended();
}
