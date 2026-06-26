using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public sealed partial class MsfxTaskSplitDialogViewModel(DialogManager dialogManager)
    : FormDialogViewModelBase(dialogManager)
{
    private static readonly MsfxTaskSplitDialogResult CancelResult =
        new(MsfxTaskSplitDialogAction.Cancel);

    private string _customQuantities = string.Empty;

    public required MsfxTaskSplitDialogModel Model { get; init; }

    public MsfxTaskSplitDialogResult? Result { get; private set; }

    public string CustomQuantities
    {
        get => _customQuantities;
        set => SetProperty(ref _customQuantities, value);
    }

    [RelayCommand]
    private void Close()
    {
        Result = CancelResult;
        CloseDialog();
    }

    [RelayCommand]
    private void SplitByBatch()
        => Complete(MsfxTaskSplitDialogAction.Batch);

    [RelayCommand]
    private void SplitCustom()
        => Complete(MsfxTaskSplitDialogAction.CustomQuantity, CustomQuantities);

    [RelayCommand]
    private void SplitByParentCluster()
        => Complete(MsfxTaskSplitDialogAction.ParentCluster);

    private void Complete(MsfxTaskSplitDialogAction action, string? customQuantities = null)
    {
        Result = new MsfxTaskSplitDialogResult(action, customQuantities);
        CloseDialog(success: action != MsfxTaskSplitDialogAction.Cancel);
    }
}
