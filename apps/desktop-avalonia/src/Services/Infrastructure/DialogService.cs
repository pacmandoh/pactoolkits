using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Views.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

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


public sealed record InfoDetailItem(string Label, string Value);

public sealed record InfoDetailDialogModel(
    string Header,
    string SubHeader,
    IReadOnlyList<InfoDetailItem> Items);

public sealed record MsfxMappingBatchDialogModel(
    IReadOnlyList<MsfxMappingBatchGroupRow> Groups,
    string MapStatusFilter,
    string CodeStatusFilter,
    string SearchScope,
    string Keyword);

public sealed record MsfxStateDetailDialogModel(
    string Header,
    string SubHeader,
    TraceEntryState State,
    string HighlightTitle,
    string HighlightMessage,
    IReadOnlyList<InfoDetailItem> Items);

public sealed class DialogService : IDialogService
{
    private readonly DialogManager _dialogManager;

    public DialogService(DialogManager dialogManager)
        => _dialogManager = dialogManager ?? throw new ArgumentNullException(nameof(dialogManager));

    public Task Info(string title, string message)
        => Ok(title, message);

    public Task Success(string title, string message)
        => Ok(title, message);

    public Task Warn(string title, string message)
        => Ok(title, message);

    public Task Error(string title, string message)
        => Ok(title, message, DialogButtonStyle.Destructive);

    public Task Ok(string title, string message)
        => Ok(title, message, DialogButtonStyle.Primary);

    public Task<bool> Confirm(string title, string message)
        => Confirm(title, message, okText: "确认", cancelText: "取消");

    public Task Ok(
        string title,
        string message,
        DialogButtonStyle primaryStyle,
        string okText = "确认")
        => ShowDialogAsync<object?>(tcs =>
        {
            _dialogManager.CreateDialog(title, message)
                .WithPrimaryButton(okText, () => tcs.TrySetResult(null), primaryStyle)
                .Dismissible()
                .Show();
        });

    public Task<bool> Confirm(
        string title,
        string message,
        string okText,
        string cancelText,
        DialogButtonStyle primaryStyle = DialogButtonStyle.Primary)
        => ShowDialogAsync<bool>(tcs =>
        {
            _dialogManager.CreateDialog(title, message)
                .WithCancelButton(cancelText, () => tcs.TrySetResult(false))
                .WithPrimaryButton(okText, () => tcs.TrySetResult(true), primaryStyle)
                .Dismissible()
                .Show();
        });

    public Task<int> Confirm3(
        string title,
        string message,
        string primaryText,
        string secondaryText,
        string cancelText)
        => ShowDialogAsync<int>(tcs =>
        {
            _dialogManager.CreateDialog(title, message)
                .WithCancelButton(cancelText, () => tcs.TrySetResult(0))
                .WithTertiaryButton(secondaryText, () => tcs.TrySetResult(2))
                .WithPrimaryButton(primaryText, () => tcs.TrySetResult(1))
                .Dismissible()
                .Show();
        });

    public Task<bool> ConfirmDrugKeyFixPreview(
        string sourceDrugId,
        string sourceSpec,
        string targetDrugId,
        string targetSpec,
        bool targetExists,
        int tracePoolAffected,
        int traceTxnAffected)
        => ShowDialogAsync<bool>(tcs =>
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

            ShowHosted(
                new PacHostedDialogContext
                {
                    Title = "纠错迁移预览详情",
                    Body = content,
                    Actions =
                    [
                        new PacHostedDialogAction
                        {
                            Text = "取消",
                            Style = DialogButtonStyle.Secondary,
                            Click = () => tcs.TrySetResult(false)
                        },
                        new PacHostedDialogAction
                        {
                            Text = "继续迁移",
                            Style = DialogButtonStyle.Primary,
                            Click = () => tcs.TrySetResult(true)
                        }
                    ]
                },
                () => tcs.TrySetResult(false));
        });

    public Task<string?> PromptInventoryUnlockPassword(string title, string hintMessage)
        => ShowDialogAsync<string?>(tcs =>
        {
            var content = new InventoryUnlockDialogView
            {
                HintMessage = hintMessage
            };
            content.SubmitRequested += () =>
            {
                tcs.TrySetResult(content.Password);
                _dialogManager.Close(content);
            };

            ShowHosted(
                new PacHostedDialogContext
                {
                    Title = title,
                    Body = content,
                    Actions =
                    [
                        new PacHostedDialogAction
                        {
                            Text = "取消",
                            Style = DialogButtonStyle.Secondary,
                            Click = () => tcs.TrySetResult(null)
                        },
                        new PacHostedDialogAction
                        {
                            Text = "验证并解锁",
                            Style = DialogButtonStyle.Primary,
                            Click = () => tcs.TrySetResult(content.Password)
                        }
                    ]
                },
                () => tcs.TrySetResult(null));
        });

    public Task InfoDetail(string title, string subHeader, IReadOnlyList<InfoDetailItem> items)
        => ShowDialogAsync<object?>(tcs =>
        {
            var content = new InfoDetailDialogView
            {
                DataContext = new InfoDetailDialogModel(
                    Header: title,
                    SubHeader: subHeader,
                    Items: items)
            };

            ShowHosted(
                new PacHostedDialogContext
                {
                    Title = title,
                    Body = content,
                    Actions =
                    [
                        new PacHostedDialogAction
                        {
                            Text = "关闭",
                            Style = DialogButtonStyle.Primary,
                            Click = () => tcs.TrySetResult(null)
                        }
                    ]
                },
                () => tcs.TrySetResult(null));
        });

    public Task ShowMsfxStateDetailDialog(MsfxStateDetailDialogModel model)
        => ShowDialogAsync<object?>(tcs =>
        {
            var content = new MsfxStateDetailDialogView
            {
                DataContext = model
            };

            ShowHosted(
                new PacHostedDialogContext
                {
                    Title = model.Header,
                    Body = content,
                    Actions =
                    [
                        new PacHostedDialogAction
                        {
                            Text = "关闭",
                            Style = DialogButtonStyle.Primary,
                            Click = () => tcs.TrySetResult(null)
                        }
                    ]
                },
                () => tcs.TrySetResult(null));
        });

    public Task<MsfxMappingBatchDialogResult> ShowMsfxMappingBatchDialog(MsfxMappingBatchDialogModel model)
        => ShowDialogAsync<MsfxMappingBatchDialogResult>(tcs =>
        {
            var content = new MsfxMappingBatchDialogView
            {
                DataContext = model
            };

            PacHostedDialogContext? context = null;
            context = new PacHostedDialogContext
            {
                Title = "批量映射",
                Body = content,
                Actions =
                [
                    new PacHostedDialogAction
                    {
                        Text = "关闭",
                        Style = DialogButtonStyle.Secondary,
                        Click = () => tcs.TrySetResult(new MsfxMappingBatchDialogResult(
                            MsfxMappingBatchDialogAction.Cancel, null, "", ""))
                    },
                    new PacHostedDialogAction
                    {
                        Text = "弃用任务",
                        Style = DialogButtonStyle.Secondary,
                        DismissOnClick = false,
                        Click = () => QueueMsfxMappingBatchDialogAction(context!, content, tcs, MsfxMappingBatchDialogAction.DiscardTask)
                    },
                    new PacHostedDialogAction
                    {
                        Text = "批量映射",
                        Style = DialogButtonStyle.Primary,
                        DismissOnClick = false,
                        Click = () => QueueMsfxMappingBatchDialogAction(context!, content, tcs, MsfxMappingBatchDialogAction.ApplyMap)
                    }
                ]
            };

            ShowHosted(context, () => tcs.TrySetResult(new MsfxMappingBatchDialogResult(
                MsfxMappingBatchDialogAction.Cancel, null, "", "")));
        });

    private void QueueMsfxMappingBatchDialogAction(
        PacHostedDialogContext context,
        MsfxMappingBatchDialogView content,
        TaskCompletionSource<MsfxMappingBatchDialogResult> tcs,
        MsfxMappingBatchDialogAction action)
        => _ = CompleteMsfxMappingBatchDialogAsync(context, content, tcs, action);

    private async Task CompleteMsfxMappingBatchDialogAsync(
        PacHostedDialogContext context,
        MsfxMappingBatchDialogView content,
        TaskCompletionSource<MsfxMappingBatchDialogResult> tcs,
        MsfxMappingBatchDialogAction action)
    {
        try
        {
            await content.PrepareForActionAsync().ConfigureAwait(true);
            tcs.TrySetResult(new MsfxMappingBatchDialogResult(
                action,
                content.SelectedGroup,
                content.DrugId,
                content.Spec));
        }
        catch (Exception ex)
        {
            AppLog.Warn("DialogService", "msfx.batch_dialog.complete.fail", "Failed to finalize MSFX batch mapping dialog action", ex);
            tcs.TrySetResult(new MsfxMappingBatchDialogResult(MsfxMappingBatchDialogAction.Cancel, null, "", ""));
        }
        finally
        {
            _dialogManager.Close(context);
        }
    }

    public Task<MsfxTaskSplitDialogResult> ShowMsfxTaskSplitDialog(MsfxTaskSplitDialogModel model)
        => ShowDialogAsync<MsfxTaskSplitDialogResult>(tcs =>
        {
            var content = new MsfxTaskSplitDialogView
            {
                DataContext = model
            };

            ShowHosted(
                new PacHostedDialogContext
                {
                    Title = "拆分任务",
                    Body = content,
                    Actions =
                    [
                        new PacHostedDialogAction
                        {
                            Text = "关闭",
                            Style = DialogButtonStyle.Secondary,
                            Click = () => tcs.TrySetResult(new MsfxTaskSplitDialogResult(MsfxTaskSplitDialogAction.Cancel))
                        },
                        new PacHostedDialogAction
                        {
                            Text = "按批号拆分",
                            Style = DialogButtonStyle.Secondary,
                            Click = () => tcs.TrySetResult(new MsfxTaskSplitDialogResult(MsfxTaskSplitDialogAction.Batch))
                        },
                        new PacHostedDialogAction
                        {
                            Text = "自定义数量拆分",
                            Style = DialogButtonStyle.Secondary,
                            Click = () => tcs.TrySetResult(new MsfxTaskSplitDialogResult(
                                MsfxTaskSplitDialogAction.CustomQuantity,
                                content.CustomQuantities))
                        },
                        new PacHostedDialogAction
                        {
                            Text = "按父码簇拆分",
                            Style = DialogButtonStyle.Primary,
                            Click = () => tcs.TrySetResult(new MsfxTaskSplitDialogResult(MsfxTaskSplitDialogAction.ParentCluster))
                        }
                    ]
                },
                () => tcs.TrySetResult(new MsfxTaskSplitDialogResult(MsfxTaskSplitDialogAction.Cancel)));
        });

    private void ShowHosted(PacHostedDialogContext context, Action onDismissed)
    {
        context.Manager = _dialogManager;
        context.RequestClose = () => _dialogManager.Close(context);

        _dialogManager.CreateDialog(context)
            .Dismissible()
            .WithCancelCallback(onDismissed)
            .Show();
    }

    private static Task<T> ShowDialogAsync<T>(Action<TaskCompletionSource<T>> show)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() => show(tcs));
        return tcs.Task;
    }
}
