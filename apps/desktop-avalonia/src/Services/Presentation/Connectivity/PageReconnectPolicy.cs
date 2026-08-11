using PacToolkits.Desktop.Avalonia.Contracts.Presentation;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

/// <summary>
/// 本机库断连与远端服务不可用时的页面可用性，以及静默刷新是否压 busy
/// </summary>
public static class PageReconnectPolicy
{
    public static PageDataAvailability DisconnectedAvailability(bool hasLoadedOnce, bool supportsStaleWhileReconnect)
        => hasLoadedOnce && supportsStaleWhileReconnect
            ? PageDataAvailability.Stale
            : PageDataAvailability.AwaitingDatabase;

    /// <summary>远端瞬时不可用：有缓存进 Stale，否则 AwaitingService</summary>
    public static PageDataAvailability ServiceUnavailableAvailability(
        bool hasLoadedOnce,
        bool supportsStaleWhileReconnect)
        => hasLoadedOnce && supportsStaleWhileReconnect
            ? PageDataAvailability.Stale
            : PageDataAvailability.AwaitingService;

    public static bool SuppressReloadBusy(
        bool hasLoadedOnce,
        bool supportsStaleWhileReconnect,
        bool reloadFromSignal)
        => reloadFromSignal && hasLoadedOnce && supportsStaleWhileReconnect;
}
