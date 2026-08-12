using PacToolkits.Desktop.Avalonia.Contracts.Presentation;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;

/// <summary>Section 空态门控文案：标题保持各区块「暂无…」，原因放 Hint</summary>
public static class SectionEmptyCopy
{
    public const string DbStaleHint = "数据库已断开，连接恢复后将自动刷新";

    public const string ServiceStaleHint = "PacApi 服务暂不可用，恢复后将自动刷新";

    public const string StaleHint = ServiceStaleHint;

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
            PageDataAvailability.AccessBlocked => AccessBlockedHint(blockReason, useServiceStale),
            PageDataAvailability.LoadFailed => string.IsNullOrWhiteSpace(loadFailedMessage)
                ? "加载失败，请稍后重试"
                : loadFailedMessage,
            PageDataAvailability.Stale => GetStaleHint(useServiceStale),
            PageDataAvailability.AwaitingService => "PacApi 服务暂不可用，就绪后将自动加载",
            PageDataAvailability.AwaitingDatabase => "数据库未连接，连接恢复后将自动加载",
            _ => readyHint ?? "暂无数据",
        };
    }

    private static string AccessBlockedHint(string? blockReason, bool useServiceStale)
    {
        if (useServiceStale)
        {
            return string.IsNullOrWhiteSpace(blockReason)
                ? "PacApi 服务不可用，恢复后将自动加载"
                : blockReason;
        }

        return string.IsNullOrWhiteSpace(blockReason)
            ? "数据库不可用，请前往设置检查数据库版本"
            : $"数据库不可用：{blockReason}，请前往设置";
    }

    public static string GetIcon(PageDataAvailability availability, bool useServiceStale = false)
        => availability switch
        {
            PageDataAvailability.AccessBlocked => "ShieldAlert",
            PageDataAvailability.LoadFailed => "CircleAlert",
            PageDataAvailability.Stale => useServiceStale ? "Server" : "Database",
            PageDataAvailability.AwaitingService => "Server",
            PageDataAvailability.AwaitingDatabase => "Database",
            _ => "Inbox",
        };
}
