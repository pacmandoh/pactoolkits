using PacToolkits.Desktop.Avalonia.Contracts.Presentation;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;

/// <summary>Section 空态门控文案</summary>
public static class SectionEmptyCopy
{
    public const string DbStaleHint = "数据库已断开，连接恢复后将自动刷新";

    public const string ServiceStaleHint = "服务暂不可用，恢复后将自动刷新";

    public const string StaleHint = DbStaleHint;

    public static string GetStaleHint(bool useServiceStale)
        => useServiceStale ? ServiceStaleHint : DbStaleHint;

    public static string GetTitle(string? readyTitle)
        => readyTitle ?? "暂无数据";

    public static string GetHint(
        PageDataAvailability availability,
        string? readyHint,
        string? blockReason = null,
        string? loadFailedMessage = null,
        bool useServiceStale = false)
    {
        return availability switch
        {
            PageDataAvailability.AccessBlocked => string.IsNullOrWhiteSpace(blockReason)
                ? "数据库不可用，请前往设置检查数据库版本"
                : $"数据库不可用：{blockReason}，请前往设置",
            PageDataAvailability.LoadFailed => string.IsNullOrWhiteSpace(loadFailedMessage)
                ? "加载失败，请使用顶部菜单刷新"
                : $"{loadFailedMessage}，请使用顶部菜单刷新",
            PageDataAvailability.Stale => GetStaleHint(useServiceStale),
            _ => readyHint ?? "暂无数据",
        };
    }

    public static string GetIcon(PageDataAvailability availability, bool useServiceStale = false)
        => availability switch
        {
            PageDataAvailability.AccessBlocked => "ShieldAlert",
            PageDataAvailability.LoadFailed => "CircleAlert",
            PageDataAvailability.Stale => useServiceStale ? "Server" : "Database",
            _ => "Inbox",
        };
}
