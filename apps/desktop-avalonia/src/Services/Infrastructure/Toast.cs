using System;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Common;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>Toast 通知入口</summary>
public interface IToastService
{
    void Success(string title, string message);
    void Error(string title, string message);
    void Warn(string title, string message);
    void Info(string title, string message);
}

/// <summary>桌面 Toast 展示实现</summary>
public sealed class ToastService : IToastService
{
    private readonly ToastManager _toasts;

    public ToastService(ToastManager toasts)
    {
        _toasts = toasts;
    }

    private static void RunOnUiThread(Action show)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            show();
        }
        else
        {
            Dispatcher.UIThread.Post(show);
        }
    }

    private static void Show(ToastManager toasts, string title, string message, Action<ToastBuilder> show)
        => RunOnUiThread(() =>
            show(toasts.CreateToast(title).WithContent(ToastContent.ForMessage(message))));

    public void Success(string title, string message)
        => Show(_toasts, title, message, static b => b.WithDelay(3).DismissOnClick().ShowSuccess());

    public void Error(string title, string message)
        => Show(_toasts, title, message, static b => b.WithDelay(3).DismissOnClick().ShowError());

    public void Warn(string title, string message)
        => Show(_toasts, title, message, static b => b.WithDelay(3).DismissOnClick().ShowWarning());

    public void Info(string title, string message)
        => Show(_toasts, title, message, static b => b.WithDelay(3).DismissOnClick().ShowInfo());
}
