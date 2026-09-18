using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

public enum ConnectivitySeverity
{
    None,
    Info,
    Warning,
    Error
}

/// <summary>
/// 未配置 / 配置无效：Warning，可进设置；已配置但不可用（Down/Blocked）：Error
/// </summary>
public sealed record ConnectivityBanner(
    bool IsVisible,
    string Title,
    string Message,
    ConnectivitySeverity Severity,
    bool ShowOpenSettings)
{
    public static ConnectivityBanner Create(
        ApiAvailabilitySnapshot snap,
        PacApiOptionsState optionsState = PacApiOptionsState.Ready,
        string? optionsError = null)
    {
        var view = ConnectionView.From(snap, optionsState, optionsError);
        return view.Kind switch
        {
            ConnectionKind.NotConfigured or ConnectionKind.Invalid => new ConnectivityBanner(
                IsVisible: true,
                Title: view.Title,
                Message: view.Message,
                Severity: ConnectivitySeverity.Warning,
                ShowOpenSettings: true),
            ConnectionKind.Blocked or ConnectionKind.Down => new ConnectivityBanner(
                IsVisible: true,
                Title: view.Title,
                Message: view.Message,
                Severity: ConnectivitySeverity.Error,
                ShowOpenSettings: false),
            _ => Hidden,
        };
    }

    private static readonly ConnectivityBanner Hidden = new(
        IsVisible: false,
        Title: string.Empty,
        Message: string.Empty,
        Severity: ConnectivitySeverity.None,
        ShowOpenSettings: false);
}
