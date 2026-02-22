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
    Task<bool> ConfirmDrugKeyFixPreview(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        bool targetExists,
        int tracePoolAffected,
        int traceTxnAffected);
    Task<string?> PromptInventoryUnlockPassword(string title, string hintMessage);
}

public sealed class DialogService : IDialogService
{
    private static readonly string[] GhostButtonClasses = { "Ghost" };
    private static readonly string[] FlatButtonClasses = { "Flat" };
    private static readonly string[] FlatAccentButtonClasses = { "Flat", "Accent" };

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
                : FlatAccentButtonClasses;

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
            okButtonClasses ??= FlatAccentButtonClasses;
            cancelButtonClasses ??= GhostButtonClasses;

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
            _dialogManager.CreateDialog()
                .OfType(NotificationType.Warning)
                .WithTitle(title)
                .WithContent(message)
                .WithActionButton(cancelText, _ => tcs.TrySetResult(0), dismissOnClick: true, classes: GhostButtonClasses)
                .WithActionButton(secondaryText, _ => tcs.TrySetResult(2), dismissOnClick: true, classes: FlatAccentButtonClasses)
                .WithActionButton(primaryText, _ => tcs.TrySetResult(1), dismissOnClick: true, classes: FlatButtonClasses)
                .Dismiss().ByClickingBackground()
                .TryShow();
        });

        return tcs.Task;
    }

    public Task<bool> ConfirmDrugKeyFixPreview(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        bool targetExists,
        int tracePoolAffected,
        int traceTxnAffected)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            var content = new DrugKeyFixPreviewDialogView
            {
                DataContext = new DrugKeyFixPreviewDialogModel(
                    SourceKeyDisplay: $"{sourceDrugId}/{sourceSpec}",
                    TargetKeyDisplay: $"{targetDrugId}/{targetSpec}",
                    TracePoolAffectedDisplay: $"{tracePoolAffected} 条",
                    TraceTxnAffectedDisplay: $"{traceTxnAffected} 条",
                    TargetExistsDisplay: targetExists
                        ? "目标药品键已存在，迁移时将并入既有记录。"
                        : "目标药品键不存在，迁移时将创建新记录。")
            };

            _dialogManager.CreateDialog()
                .OfType(NotificationType.Warning)
                .WithTitle("纠错迁移预览详情")
                .WithContent(content)
                .WithActionButton("取消", _ => tcs.TrySetResult(false), dismissOnClick: true, classes: GhostButtonClasses)
                .WithActionButton("继续迁移", _ => tcs.TrySetResult(true), dismissOnClick: true, classes: FlatAccentButtonClasses)
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
                .WithActionButton("取消", _ => tcs.TrySetResult(null), dismissOnClick: true, classes: GhostButtonClasses)
                .WithActionButton("验证并解锁", _ => tcs.TrySetResult(content.Password), dismissOnClick: true, classes: FlatAccentButtonClasses)
                .TryShow();
        });

        return tcs.Task;
    }
}
