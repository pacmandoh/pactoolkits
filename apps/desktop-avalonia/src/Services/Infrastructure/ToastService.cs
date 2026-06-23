using System;
using Avalonia.Threading;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

public interface IToastService
{
    void Success(string title, string message);
    void Error(string title, string message);
    void Warn(string title, string message);
    void Info(string title, string message);
}

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

    public void Success(string title, string message)
        => RunOnUiThread(() =>
            _toasts.CreateToast(title)
                .WithContent(message)
                .WithDelay(3)
                .DismissOnClick()
                .ShowSuccess());

    public void Error(string title, string message)
        => RunOnUiThread(() =>
            _toasts.CreateToast(title)
                .WithContent(message)
                .WithDelay(3)
                .DismissOnClick()
                .ShowError());

    public void Warn(string title, string message)
        => RunOnUiThread(() =>
            _toasts.CreateToast(title)
                .WithContent(message)
                .WithDelay(3)
                .DismissOnClick()
                .ShowWarning());

    public void Info(string title, string message)
        => RunOnUiThread(() =>
            _toasts.CreateToast(title)
                .WithContent(message)
                .WithDelay(3)
                .DismissOnClick()
                .ShowInfo());
}
