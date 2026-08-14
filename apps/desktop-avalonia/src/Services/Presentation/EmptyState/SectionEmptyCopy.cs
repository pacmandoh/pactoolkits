using PacToolkits.Desktop.Avalonia.Contracts.Presentation;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;

/// <summary>Section 空态门控文案：标题保持各区块「暂无…」，原因放 Hint</summary>
public static class SectionEmptyCopy
{
    public const string StaleHint = "PacAPI 服务暂不可用，恢复后将自动刷新";

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
                ? "PacAPI 服务不可用，恢复后将自动加载"
                : blockReason,
            PageDataAvailability.LoadFailed => string.IsNullOrWhiteSpace(loadFailedMessage)
                ? "加载失败，请稍后重试"
                : loadFailedMessage,
            PageDataAvailability.Stale => StaleHint,
            PageDataAvailability.AwaitingService => "PacAPI 服务暂不可用，就绪后将自动加载",
            _ => readyHint ?? "暂无数据",
        };
    }

    public static string GetIcon(PageDataAvailability availability)
        => availability switch
        {
            PageDataAvailability.AccessBlocked => "ShieldAlert",
            PageDataAvailability.LoadFailed => "CircleAlert",
            PageDataAvailability.Stale => "Server",
            PageDataAvailability.AwaitingService => "Server",
            _ => "Inbox",
        };
}
