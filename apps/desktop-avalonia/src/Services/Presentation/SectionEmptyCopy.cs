using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

public static class SectionEmptyCopy
{
    public const string StaleHint = "数据库已断开，连接恢复后将自动刷新";

    public static string GetTitle(string? readyTitle)
        => readyTitle ?? "暂无数据";

    public static string GetHint(
        PageDataAvailability availability,
        string? readyHint,
        string? blockReason = null,
        string? loadFailedMessage = null)
    {
        return availability switch
        {
            PageDataAvailability.AccessBlocked => string.IsNullOrWhiteSpace(blockReason)
                ? "数据库不可用，请前往设置检查数据库版本"
                : $"数据库不可用：{blockReason}，请前往设置",
            PageDataAvailability.LoadFailed => string.IsNullOrWhiteSpace(loadFailedMessage)
                ? "加载失败，请使用顶部菜单刷新"
                : $"{loadFailedMessage}，请使用顶部菜单刷新",
            PageDataAvailability.Stale => StaleHint,
            _ => readyHint ?? "暂无数据",
        };
    }

    public static string GetIcon(PageDataAvailability availability)
        => availability switch
        {
            PageDataAvailability.AccessBlocked => "ShieldAlert",
            PageDataAvailability.LoadFailed => "CircleAlert",
            PageDataAvailability.Stale => "Database",
            _ => "Inbox",
        };
}
