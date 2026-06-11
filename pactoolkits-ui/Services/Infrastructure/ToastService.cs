using System;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using SukiUI.Toasts;

namespace pactoolkits_ui.Services.Infrastructure;

public interface IToastService
{
    void Success(string title, string message);
    void Error(string title, string message);
    void Warn(string title, string message);
    void Info(string title, string message);
}

public sealed class ToastService : IToastService
{
    private readonly ISukiToastManager _toasts;

    public ToastService(ISukiToastManager toasts)
    {
        _toasts = toasts;
    }

    private static void RunOnUiThread(Action show)
    {
        if (Dispatcher.UIThread.CheckAccess())
            show();
        else
            Dispatcher.UIThread.Post(show);
    }

    public void Success(string title, string message)
        => RunOnUiThread(() =>
            _toasts.CreateToast()
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .OfType(NotificationType.Success)
                .WithTitle(title)
                .WithContent(message)
                .Queue());

    public void Error(string title, string message)
        => RunOnUiThread(() =>
            _toasts.CreateToast()
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .OfType(NotificationType.Error)
                .WithTitle(title)
                .WithContent(message)
                .Queue());

    public void Warn(string title, string message)
        => RunOnUiThread(() =>
            _toasts.CreateToast()
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .OfType(NotificationType.Warning)
                .WithTitle(title)
                .WithContent(message)
                .Queue());

    public void Info(string title, string message)
        => RunOnUiThread(() =>
            _toasts.CreateSimpleInfoToast()
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Dismiss().ByClicking()
                .WithTitle(title)
                .WithContent(message)
                .Queue());
}
