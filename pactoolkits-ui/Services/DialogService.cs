using System;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using SukiUI.Dialogs;
using pactoolkits_ui.Views.Dialogs;

namespace pactoolkits_ui.Services;

public interface IDialogService
{
    Task Info(string title, string message);
    Task Success(string title, string message);
    Task Warn(string title, string message);
    Task Error(string title, string message);

    Task Ok(string title, string message);
    Task<bool> Confirm(string title, string message);
    Task<int> Confirm3(string title, string message, string primaryText, string secondaryText, string cancelText);
    Task<string?> PromptInventoryUnlockPassword(string title, string hintMessage);
}

public sealed class DialogService : IDialogService
{
    private readonly ISukiDialogManager _dialogManager;

    public DialogService(ISukiDialogManager dialogManager)
        => _dialogManager = dialogManager ?? throw new ArgumentNullException(nameof(dialogManager));

    public Task Info(string title, string message)
        => Ok(title, message, NotificationType.Information);

    public Task Success(string title, string message)
        => Ok(title, message, NotificationType.Success);

    public Task Warn(string title, string message)
        => Ok(title, message, NotificationType.Warning);

    public Task Error(string title, string message)
        => Ok(title, message, NotificationType.Error);

    public Task Ok(string title, string message)
        => Ok(title, message, NotificationType.Information);

    public Task<bool> Confirm(string title, string message)
        => Confirm(title, message, okText: "确认", cancelText: "取消");

    public Task Ok(
        string title,
        string message,
        NotificationType type,
        string okText = "确认",
        params string[] okButtonClasses)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            var classes = (okButtonClasses is { Length: > 0 })
                ? okButtonClasses
                : new[] { "Flat", "Accent" };

            _dialogManager.CreateDialog()
                .OfType(type)
                .WithTitle(title)
                .WithContent(message)
                .WithActionButton(okText, _ => tcs.TrySetResult(null), dismissOnClick: true, classes: classes)
                .Dismiss().ByClickingBackground()
                .TryShow();
        });

        return tcs.Task;
    }

    public Task<bool> Confirm(
        string title,
        string message,
        string okText,
        string cancelText,
        NotificationType type = NotificationType.Warning,
        string[]? okButtonClasses = null,
        string[]? cancelButtonClasses = null)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            okButtonClasses ??= new[] { "Flat", "Accent" };
            cancelButtonClasses ??= new[] { "Ghost" };

            _dialogManager.CreateDialog()
                .OfType(type)
                .WithTitle(title)
                .WithContent(message)
                .WithActionButton(cancelText, _ => tcs.TrySetResult(false), dismissOnClick: true, classes: cancelButtonClasses)
                .WithActionButton(okText, _ => tcs.TrySetResult(true), dismissOnClick: true, classes: okButtonClasses)
                .Dismiss().ByClickingBackground()
                .TryShow();
        });

        return tcs.Task;
    }

    public Task<int> Confirm3(
        string title,
        string message,
        string primaryText,
        string secondaryText,
        string cancelText)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            var primaryClasses = new[] { "Flat" };
            var secondaryClasses = new[] { "Flat", "Accent" };
            var cancelClasses = new[] { "Ghost" };

            _dialogManager.CreateDialog()
                .OfType(NotificationType.Warning)
                .WithTitle(title)
                .WithContent(message)
                .WithActionButton(cancelText, _ => tcs.TrySetResult(0), dismissOnClick: true, classes: cancelClasses)
                .WithActionButton(secondaryText, _ => tcs.TrySetResult(2), dismissOnClick: true, classes: secondaryClasses)
                .WithActionButton(primaryText, _ => tcs.TrySetResult(1), dismissOnClick: true, classes: primaryClasses)
                .Dismiss().ByClickingBackground()
                .TryShow();
        });

        return tcs.Task;
    }

    public Task<string?> PromptInventoryUnlockPassword(string title, string hintMessage)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            var content = new InventoryUnlockDialogView
            {
                HintMessage = hintMessage
            };
            content.SubmitRequested += () =>
            {
                tcs.TrySetResult(content.Password);
                _dialogManager.DismissDialog();
            };

            _dialogManager.CreateDialog()
                .OfType(NotificationType.Information)
                .WithTitle(title)
                .WithContent(content)
                .WithActionButton("取消", _ => tcs.TrySetResult(null), dismissOnClick: true, classes: new[] { "Ghost" })
                .WithActionButton("验证并解锁", _ => tcs.TrySetResult(content.Password), dismissOnClick: true, classes: new[] { "Flat", "Accent" })
                .TryShow();
        });

        return tcs.Task;
    }
}
