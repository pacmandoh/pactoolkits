using PacToolkits.Application.Abstractions;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public enum ConnectivitySeverity
{
    None,
    Info,
    Warning,
    Error
}

public sealed record ConnectivityBanner(
    bool IsVisible,
    string Title,
    string Message,
    ConnectivitySeverity Severity,
    bool ShowOpenSettings);

public static class ConnectivityBannerFactory
{
    public static ConnectivityBanner Create(
        bool isDbConnected,
        bool isConnectivityKnown,
        IDbAccessGuard accessGuard)
    {
        if (accessGuard.IsBlocked)
        {
            return new ConnectivityBanner(
                IsVisible: true,
                Title: "数据库不可用",
                Message: accessGuard.BlockReason ?? "数据库版本不兼容，业务操作已阻断",
                Severity: ConnectivitySeverity.Error,
                ShowOpenSettings: true);
        }

        if (!isDbConnected)
        {
            if (!isConnectivityKnown)
            {
                return HiddenBanner;
            }

            return new ConnectivityBanner(
                IsVisible: true,
                Title: "数据库未连接",
                Message: "正在等待重连，恢复后页面将自动刷新",
                Severity: ConnectivitySeverity.Warning,
                ShowOpenSettings: true);
        }

        return HiddenBanner;
    }

    private static readonly ConnectivityBanner HiddenBanner = new(
        IsVisible: false,
        Title: string.Empty,
        Message: string.Empty,
        Severity: ConnectivitySeverity.None,
        ShowOpenSettings: false);
}
