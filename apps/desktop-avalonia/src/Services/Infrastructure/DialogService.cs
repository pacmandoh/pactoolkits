using System.Collections.Generic;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services.Msfx;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;
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
    Task<string?> PromptUnlockPassword(string title, string hintMessage);
    Task InfoDetail(string title, string subHeader, IReadOnlyList<InfoDetailItem> items);
    Task ShowMsfxStateDetail(MsfxStateDetailArgs model);
    Task<MsfxMappingBatchResult> ShowMsfxMappingBatch(MsfxMappingBatchArgs model);
    Task<MsfxTaskSplitResult> ShowMsfxTaskSplit(MsfxTaskSplitArgs model);
}

public sealed class DialogService(
    DialogManager dialogManager,
    SensitiveUnlock unlockDialog,
    ILookupCatalogService lookup,
    ISyncService syncService,
    IDbAccessGuard accessGuard) : IDialogService
{
    private const double AlertMaxWidth = 512;
    private const double DetailMaxWidth = 768;
    private const double WideFormMaxWidth = 1280;

    private static readonly MsfxMappingBatchResult MsfxMappingBatchCancelResult = new(
        MsfxMappingBatchAction.Cancel, null, string.Empty, string.Empty);

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
        => Alert(AlertBuilder<bool>.Create(title, message).Close(false).Danger("确认", true));

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

    public Task<string?> PromptUnlockPassword(string title, string hintMessage)
        => FormDialogSession.ShowAsync(
            dialogManager,
            unlockDialog,
            vm => vm.Initialize(title, hintMessage),
            static vm => vm.Password,
            static () => (string?)null);

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
            maxWidth: DetailMaxWidth).ConfigureAwait(true);
    }

    public Task<MsfxMappingBatchResult> ShowMsfxMappingBatch(MsfxMappingBatchArgs model)
        => FormDialogSession.ShowAsync(
            dialogManager,
            new MsfxMappingBatch(dialogManager, lookup, syncService, accessGuard) { Args = model },
            prepare: null,
            onSuccess: vm => vm.Result ?? MsfxMappingBatchCancelResult,
            onCancel: () => MsfxMappingBatchCancelResult,
            maxWidth: WideFormMaxWidth);

    public Task<MsfxTaskSplitResult> ShowMsfxTaskSplit(MsfxTaskSplitArgs model)
        => FormDialogSession.ShowAsync(
            dialogManager,
            new MsfxTaskSplit(dialogManager) { Args = model },
            prepare: null,
            onSuccess: vm => vm.Result ?? MsfxTaskSplitCancelResult,
            onCancel: () => MsfxTaskSplitCancelResult,
            maxWidth: WideFormMaxWidth);
}
