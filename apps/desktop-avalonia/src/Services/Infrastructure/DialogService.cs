using System.Collections.Generic;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;
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
    Task<bool> ConfirmDestructive(string title, string message);
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

public sealed class DialogService(
    DialogManager dialogManager,
    InventoryUnlockDialogViewModel unlockDialog,
    ILookupCatalogService lookup,
    IMsfxSyncService syncService,
    IDbAccessGuard accessGuard) : IDialogService
{
    private const double AlertMaxWidth = 512;
    private const double DetailMaxWidth = 768;
    private const double WideFormMaxWidth = 1280;

    private static readonly MsfxMappingBatchDialogResult MsfxMappingBatchCancelResult = new(
        MsfxMappingBatchDialogAction.Cancel, null, string.Empty, string.Empty);

    private static readonly MsfxTaskSplitDialogResult MsfxTaskSplitCancelResult =
        new(MsfxTaskSplitDialogAction.Cancel);

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

    public Task<bool> ConfirmDestructive(string title, string message)
        => Confirm(title, message, okText: "确认", cancelText: "取消", DialogButtonStyle.Destructive);

    public Task Ok(
        string title,
        string message,
        DialogButtonStyle primaryStyle,
        string okText = "确认")
        => DialogAwaiter.RunAlertAsync<object?>(dialogManager, tcs =>
        {
            dialogManager.CreateDialog(title, message)
                .WithPrimaryButton(okText, () => tcs.TrySetResult(null), primaryStyle)
                .WithMaxWidth(AlertMaxWidth)
                .Dismissible()
                .Show();
        });

    public Task<bool> Confirm(
        string title,
        string message,
        string okText,
        string cancelText,
        DialogButtonStyle primaryStyle = DialogButtonStyle.Primary)
        => DialogAwaiter.RunAlertAsync<bool>(dialogManager, tcs =>
        {
            dialogManager.CreateDialog(title, message)
                .WithCancelButton(cancelText, () => tcs.TrySetResult(false))
                .WithPrimaryButton(okText, () => tcs.TrySetResult(true), primaryStyle)
                .WithMaxWidth(AlertMaxWidth)
                .Dismissible()
                .Show();
        });

    public Task<int> Confirm3(
        string title,
        string message,
        string primaryText,
        string secondaryText,
        string cancelText)
        => DialogAwaiter.RunAlertAsync<int>(dialogManager, tcs =>
        {
            dialogManager.CreateDialog(title, message)
                .WithCancelButton(cancelText, () => tcs.TrySetResult(0))
                .WithTertiaryButton(secondaryText, () => tcs.TrySetResult(2))
                .WithPrimaryButton(primaryText, () => tcs.TrySetResult(1))
                .WithMaxWidth(AlertMaxWidth)
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
        => FormDialogSession.ShowAsync(
            dialogManager,
            new DrugKeyFixPreviewDialogViewModel(dialogManager)
            {
                Preview = new DrugKeyFixPreviewDialogModel(
                    SourceKeyDisplay: $"{sourceDrugId}/{sourceSpec}",
                    TargetKeyDisplay: $"{targetDrugId}/{targetSpec}",
                    TracePoolAffectedDisplay: $"{tracePoolAffected} 条",
                    TraceTxnAffectedDisplay: $"{traceTxnAffected} 条",
                    TargetExistsDisplay: targetExists
                        ? "目标药品键已存在，迁移时将并入既有记录"
                        : "目标药品键不存在，迁移时将创建新记录")
            },
            prepare: null,
            onSuccess: static _ => true,
            onCancel: static () => false);

    public Task<string?> PromptInventoryUnlockPassword(string title, string hintMessage)
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
            new InfoDetailDialogViewModel(dialogManager)
            {
                Detail = new InfoDetailDialogModel(title, subHeader, items)
            },
            prepare: null,
            onSuccess: static _ => true,
            onCancel: static () => false,
            maxWidth: DetailMaxWidth).ConfigureAwait(true);
    }

    public async Task ShowMsfxStateDetailDialog(MsfxStateDetailDialogModel model)
    {
        await FormDialogSession.ShowAsync(
            dialogManager,
            new MsfxStateDetailDialogViewModel(dialogManager) { Detail = model },
            prepare: null,
            onSuccess: static _ => true,
            onCancel: static () => false,
            maxWidth: DetailMaxWidth).ConfigureAwait(true);
    }

    public Task<MsfxMappingBatchDialogResult> ShowMsfxMappingBatchDialog(MsfxMappingBatchDialogModel model)
        => FormDialogSession.ShowAsync(
            dialogManager,
            new MsfxMappingBatchDialogViewModel(dialogManager, lookup, syncService, accessGuard) { Model = model },
            prepare: null,
            onSuccess: vm => vm.Result ?? MsfxMappingBatchCancelResult,
            onCancel: () => MsfxMappingBatchCancelResult,
            maxWidth: WideFormMaxWidth);

    public Task<MsfxTaskSplitDialogResult> ShowMsfxTaskSplitDialog(MsfxTaskSplitDialogModel model)
        => FormDialogSession.ShowAsync(
            dialogManager,
            new MsfxTaskSplitDialogViewModel(dialogManager) { Model = model },
            prepare: null,
            onSuccess: vm => vm.Result ?? MsfxTaskSplitCancelResult,
            onCancel: () => MsfxTaskSplitCancelResult,
            maxWidth: WideFormMaxWidth);
}
