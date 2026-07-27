using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

/// <summary>
/// 定义页面断开数据库连接后的缓存展示与重载状态抑制规则
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
