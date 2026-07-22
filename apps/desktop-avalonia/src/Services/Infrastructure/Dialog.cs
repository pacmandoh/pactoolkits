using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure;

/// <summary>桌面对话框展示入口</summary>
public interface IDialogService
{
    Task Info(string title, string message);
    Task Success(string title, string message);
    Task Warn(string title, string message);
    Task Error(string title, string message);

    Task Ok(string title, string message);
    Task<bool> Confirm(string title, string message);
    Task<bool> Confirm(string title, string message, string okText, string cancelText, DialogButtonStyle primaryStyle = DialogButtonStyle.Primary);
    Task<bool> ConfirmDestructive(string title, string message);
    Task<T> Alert<T>(AlertBuilder<T> alert);
    Task<bool> ConfirmDrugKeyFixPreview(
        string sourceDrugId,
        string sourceSpec,
        int sourceQty,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        bool targetExists,
        int tracePoolAffected,
        int traceTxnAffected);
    Task<string?> PromptUnlockPassword(
        string title,
        string hintMessage,
        Func<string, string?>? verify = null);
    Task ShowAppInfo(AppInfoArgs model);
    Task InfoDetail(string title, string subHeader, IReadOnlyList<InfoDetailItem> items);
    Task ShowMsfxStateDetail(MsfxStateDetailArgs model);
    Task<MsfxTaskSplitResult> ShowMsfxTaskSplit(MsfxTaskSplitArgs model);
}

/// <summary>
/// 对话框服务
///
/// 负责 Alert/表单对话框编排；不含业务校验
/// </summary>
public sealed class DialogService(
    DialogManager dialogManager,
    SensitiveUnlock unlockDialog) : IDialogService
{
    private const double AlertMaxWidth = 512;
    private const double DetailMaxWidth = 768;
    private const double TaskSplitMaxWidth = 1024;

    private static readonly MsfxTaskSplitResult MsfxTaskSplitCancelResult =
        new(MsfxTaskSplitAction.Cancel);

    public Task Info(string title, string message)
        => Ok(title, message);

    public Task Success(string title, string message)
        => Ok(title, message);

    public Task Warn(string title, string message)
        => Alert(AlertBuilder<object?>.Create(title, message).Close(null).Affirm("确认", null));

    public Task Error(string title, string message)
        => Alert(AlertBuilder<object?>.Create(title, message).Close(null).Danger("确认", null));

    public Task Ok(string title, string message)
        => Alert(AlertBuilder<object?>.Create(title, message).Close(null).Ack("确认", null));

    public Task<bool> Confirm(string title, string message)
        => Confirm(title, message, okText: "确认", cancelText: "取消");

    public Task<bool> ConfirmDestructive(string title, string message)
        => Confirm(title, message, okText: "确认", cancelText: "取消", primaryStyle: DialogButtonStyle.Destructive);

    public Task<T> Alert<T>(AlertBuilder<T> alert)
        => AlertSession.ShowAsync(dialogManager, alert, AlertMaxWidth);

    public Task<bool> Confirm(
        string title,
        string message,
        string okText,
        string cancelText,
        DialogButtonStyle primaryStyle = DialogButtonStyle.Primary)
    {
        var builder = AlertBuilder<bool>.Create(title, message).Close(false).Cancel(cancelText, false);

        return Alert(primaryStyle == DialogButtonStyle.Destructive
            ? builder.Danger(okText, true)
            : builder.Affirm(okText, true));
    }

    public Task<bool> ConfirmDrugKeyFixPreview(
        string sourceDrugId,
        string sourceSpec,
        int sourceQty,
        string targetDrugId,
        string targetSpec,
        int targetQty,
        bool targetExists,
        int tracePoolAffected,
        int traceTxnAffected)
        => FormDialogSession.ShowAsync(
            dialogManager,
            new DrugKeyFixPreview(dialogManager)
            {
                Preview = new DrugKeyFixPreviewArgs(
                    SourceKeyDisplay: DrugLabel.WithQty(sourceDrugId, sourceSpec, sourceQty),
                    TargetKeyDisplay: DrugLabel.WithQty(targetDrugId, targetSpec, targetQty),
                    TracePoolAffectedDisplay: $"{tracePoolAffected} 条",
                    TraceTxnAffectedDisplay: $"{traceTxnAffected} 条",
                    TargetExistsDisplay: targetExists
                        ? "目标药品键已存在，迁移时将并入既有记录"
                        : "目标药品键不存在，迁移时将创建新记录")
            },
            prepare: null,
            onSuccess: static _ => true,
            onCancel: static () => false);

    public Task<string?> PromptUnlockPassword(
        string title,
        string hintMessage,
        Func<string, string?>? verify = null)
        => ClearUnlockPasswordAfterPromptAsync(
            FormDialogSession.ShowAsync(
                dialogManager,
                unlockDialog,
                vm => vm.Initialize(title, hintMessage, verify),
                static vm => vm.Password.Trim(),
                static () => (string?)null));

    private async Task<string?> ClearUnlockPasswordAfterPromptAsync(Task<string?> prompt)
    {
        try
        {
            return await prompt.ConfigureAwait(true);
        }
        finally
        {
            // 单例解锁 VM 会保留输入，除非每次 session 在此清空
            unlockDialog.ClearSensitiveState();
        }
    }

    public async Task ShowAppInfo(AppInfoArgs model)
    {
        await FormDialogSession.ShowAsync(
            dialogManager,
            new AppInfo(dialogManager) { Info = model },
            prepare: null,
            onSuccess: static _ => true,
            onCancel: static () => false,
            maxWidth: AlertMaxWidth,
            dismissible: true).ConfigureAwait(true);
    }

    public async Task InfoDetail(string title, string subHeader, IReadOnlyList<InfoDetailItem> items)
    {
        await FormDialogSession.ShowAsync(
            dialogManager,
            new InfoDetail(dialogManager)
            {
                Detail = new InfoDetailArgs(title, subHeader, items)
            },
            prepare: null,
            onSuccess: static _ => true,
            onCancel: static () => false,
            maxWidth: DetailMaxWidth).ConfigureAwait(true);
    }

    public async Task ShowMsfxStateDetail(MsfxStateDetailArgs model)
    {
        await FormDialogSession.ShowAsync(
            dialogManager,
            new MsfxStateDetail(dialogManager) { Detail = model },
            prepare: null,
            onSuccess: static _ => true,
            onCancel: static () => false,
            maxWidth: DetailMaxWidth,
            dismissible: true).ConfigureAwait(true);
    }

    public Task<MsfxTaskSplitResult> ShowMsfxTaskSplit(MsfxTaskSplitArgs model)
        => FormDialogSession.ShowAsync(
            dialogManager,
            new MsfxTaskSplit(dialogManager) { Args = model },
            prepare: null,
            onSuccess: vm => vm.Result ?? MsfxTaskSplitCancelResult,
            onCancel: () => MsfxTaskSplitCancelResult,
            maxWidth: TaskSplitMaxWidth);
}
