using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class PageStaleWhileReconnectPolicy
{
    public static PageDataAvailability DisconnectedAvailability(bool hasLoadedOnce, bool supportsStaleWhileReconnect)
        => hasLoadedOnce && supportsStaleWhileReconnect
            ? PageDataAvailability.Stale
            : PageDataAvailability.AwaitingDatabase;

    public static bool ShouldSuppressReloadBusy(
        bool hasLoadedOnce,
        bool supportsStaleWhileReconnect,
        bool reloadFromDbSignal)
        => reloadFromDbSignal && hasLoadedOnce && supportsStaleWhileReconnect;
}
