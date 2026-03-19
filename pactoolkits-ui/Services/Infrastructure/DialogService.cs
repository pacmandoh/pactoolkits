using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using SukiUI.Dialogs;
using pactoolkits_ui.Contracts;
using pactoolkits_ui.Repositories;
using pactoolkits_ui.Views.Dialogs;

namespace pactoolkits_ui.Services.Infrastructure;

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
    Task InfoDetail(string title, string subHeader, IReadOnlyList<InfoDetailItem> items);
    Task ShowMsfxStateDetailDialog(MsfxStateDetailDialogModel model);
    Task<MsfxMappingBatchDialogResult> ShowMsfxMappingBatchDialog(MsfxMappingBatchDialogModel model);
    Task<MsfxTaskSplitDialogResult> ShowMsfxTaskSplitDialog(MsfxTaskSplitDialogModel model);
}

public enum MsfxMappingBatchDialogAction
{
    Cancel = 0,
    DiscardTask = 1,
    ApplyMap = 2
}

public sealed record MsfxMappingBatchDialogResult(
    MsfxMappingBatchDialogAction Action,
    MsfxMappingBatchGroupRow? Group,
    string DrugId,
    string Spec);

public enum MsfxTaskSplitDialogAction
{
    Cancel = 0,
    ParentCluster = 1,
    Batch = 2,
    CustomQuantity = 3
}

public sealed record MsfxTaskSplitDialogModel(
    long TaskId,
    string SourceBillCode,
    string Target,
    int TotalCodes,
    IReadOnlyList<MsfxInjectTaskSplitCodeRow> SplitCodeRows);

public sealed record MsfxTaskSplitDialogResult(
    MsfxTaskSplitDialogAction Action,
    string? CustomQuantities = null);

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
                .OnDismissed(_ => tcs.TrySetResult(null))
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
                .OnDismissed(_ => tcs.TrySetResult(false))
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
                .OnDismissed(_ => tcs.TrySetResult(0))
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
                        ? "目标药品键已存在，迁移时将并入既有记录"
                        : "目标药品键不存在，迁移时将创建新记录")
            };

            _dialogManager.CreateDialog()
                .OfType(NotificationType.Warning)
                .WithTitle("纠错迁移预览详情")
                .WithContent(content)
                .WithActionButton("取消", _ => tcs.TrySetResult(false), dismissOnClick: true, classes: GhostButtonClasses)
                .WithActionButton("继续迁移", _ => tcs.TrySetResult(true), dismissOnClick: true, classes: FlatAccentButtonClasses)
                .Dismiss().ByClickingBackground()
                .OnDismissed(_ => tcs.TrySetResult(false))
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

    public Task InfoDetail(string title, string subHeader, IReadOnlyList<InfoDetailItem> items)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            var content = new InfoDetailDialogView
            {
                DataContext = new InfoDetailDialogModel(
                    Header: title,
                    SubHeader: subHeader,
                    Items: items)
            };

            _dialogManager.CreateDialog()
                .OfType(NotificationType.Information)
                .WithTitle(title)
                .WithContent(content)
                .WithActionButton("关闭", _ => tcs.TrySetResult(null), dismissOnClick: true, classes: FlatAccentButtonClasses)
                .Dismiss().ByClickingBackground()
                .OnDismissed(_ => tcs.TrySetResult(null))
                .TryShow();
        });

        return tcs.Task;
    }

    public Task ShowMsfxStateDetailDialog(MsfxStateDetailDialogModel model)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            var content = new MsfxStateDetailDialogView
            {
                DataContext = model
            };

            _dialogManager.CreateDialog()
                .OfType(ToNotificationType(model.State))
                .WithTitle(model.Header)
                .WithContent(content)
                .WithActionButton("关闭", _ => tcs.TrySetResult(null), dismissOnClick: true, classes: FlatAccentButtonClasses)
                .Dismiss().ByClickingBackground()
                .OnDismissed(_ => tcs.TrySetResult(null))
                .TryShow();
        });

        return tcs.Task;
    }

    public Task<MsfxMappingBatchDialogResult> ShowMsfxMappingBatchDialog(MsfxMappingBatchDialogModel model)
    {
        var tcs = new TaskCompletionSource<MsfxMappingBatchDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            var content = new MsfxMappingBatchDialogView
            {
                DataContext = model
            };
            MsfxMappingBatchGroupRow? ResolveGroup()
                => content.SelectedGroup;

            _dialogManager.CreateDialog()
                .OfType(NotificationType.Information)
                .WithTitle("批量映射")
                .WithContent(content)
                .WithActionButton("关闭", _ => tcs.TrySetResult(new MsfxMappingBatchDialogResult(MsfxMappingBatchDialogAction.Cancel, null, "", "")), dismissOnClick: true, classes: GhostButtonClasses)
                .WithActionButton("弃用任务", _ => tcs.TrySetResult(new MsfxMappingBatchDialogResult(MsfxMappingBatchDialogAction.DiscardTask, ResolveGroup(), content.DrugId, content.Spec)), dismissOnClick: true, classes: FlatButtonClasses)
                .WithActionButton("批量映射", _ => tcs.TrySetResult(new MsfxMappingBatchDialogResult(MsfxMappingBatchDialogAction.ApplyMap, ResolveGroup(), content.DrugId, content.Spec)), dismissOnClick: true, classes: FlatAccentButtonClasses)
                .Dismiss().ByClickingBackground()
                .OnDismissed(_ => tcs.TrySetResult(new MsfxMappingBatchDialogResult(MsfxMappingBatchDialogAction.Cancel, null, "", "")))
                .TryShow();
        });

        return tcs.Task;
    }

    public Task<MsfxTaskSplitDialogResult> ShowMsfxTaskSplitDialog(MsfxTaskSplitDialogModel model)
    {
        var tcs = new TaskCompletionSource<MsfxTaskSplitDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        Dispatcher.UIThread.Post(() =>
        {
            var content = new MsfxTaskSplitDialogView
            {
                DataContext = model
            };

            _dialogManager.CreateDialog()
                .OfType(NotificationType.Information)
                .WithTitle("拆分任务")
                .WithContent(content)
                .WithActionButton("关闭", _ => tcs.TrySetResult(new MsfxTaskSplitDialogResult(MsfxTaskSplitDialogAction.Cancel)), dismissOnClick: true, classes: GhostButtonClasses)
                .WithActionButton("按批号拆分", _ => tcs.TrySetResult(new MsfxTaskSplitDialogResult(MsfxTaskSplitDialogAction.Batch)), dismissOnClick: true, classes: FlatButtonClasses)
                .WithActionButton("自定义数量拆分", _ => tcs.TrySetResult(new MsfxTaskSplitDialogResult(MsfxTaskSplitDialogAction.CustomQuantity, content.CustomQuantities)), dismissOnClick: true, classes: FlatButtonClasses)
                .WithActionButton("按父码簇拆分", _ => tcs.TrySetResult(new MsfxTaskSplitDialogResult(MsfxTaskSplitDialogAction.ParentCluster)), dismissOnClick: true, classes: FlatAccentButtonClasses)
                .Dismiss().ByClickingBackground()
                .OnDismissed(_ => tcs.TrySetResult(new MsfxTaskSplitDialogResult(MsfxTaskSplitDialogAction.Cancel)))
                .TryShow();
        });

        return tcs.Task;
    }

    private static NotificationType ToNotificationType(TraceEntryState state)
        => state switch
        {
            TraceEntryState.Success => NotificationType.Success,
            TraceEntryState.Warning => NotificationType.Warning,
            TraceEntryState.Failed => NotificationType.Error,
            TraceEntryState.Discarded => NotificationType.Information,
            _ => NotificationType.Information
        };
}
