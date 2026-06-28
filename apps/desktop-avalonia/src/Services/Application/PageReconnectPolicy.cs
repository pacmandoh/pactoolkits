using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class PageReconnectPolicy
{
    public static PageDataAvailability DisconnectedAvailability(bool hasLoadedOnce, bool supportsStaleWhileReconnect)
        => hasLoadedOnce && supportsStaleWhileReconnect
            ? PageDataAvailability.Stale
            : PageDataAvailability.AwaitingDatabase;

    public static bool SuppressReloadBusy(
        bool hasLoadedOnce,
        bool supportsStaleWhileReconnect,
        bool reloadFromDbSignal)
        => reloadFromDbSignal && hasLoadedOnce && supportsStaleWhileReconnect;
}
