using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

/// <summary>
/// 页面共用的断连后 stale 展示与 reload busy 抑制规则
/// </summary>
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
