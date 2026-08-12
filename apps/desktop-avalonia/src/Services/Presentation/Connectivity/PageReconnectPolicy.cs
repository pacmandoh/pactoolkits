using PacToolkits.Desktop.Avalonia.Contracts.Presentation;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

/// <summary>
/// 断连后的页面可用性
/// </summary>
public static class PageReconnectPolicy
{
    public static PageDataAvailability DisconnectedAvailability(bool hasLoadedOnce, bool supportsStaleWhileReconnect)
        => hasLoadedOnce && supportsStaleWhileReconnect
            ? PageDataAvailability.Stale
            : PageDataAvailability.AwaitingDatabase;

    /// <summary>服务瞬时不可用：有缓存进 Stale，否则 AwaitingService</summary>
    public static PageDataAvailability ServiceUnavailableAvailability(
        bool hasLoadedOnce,
        bool supportsStaleWhileReconnect)
        => hasLoadedOnce && supportsStaleWhileReconnect
            ? PageDataAvailability.Stale
            : PageDataAvailability.AwaitingService;
}
