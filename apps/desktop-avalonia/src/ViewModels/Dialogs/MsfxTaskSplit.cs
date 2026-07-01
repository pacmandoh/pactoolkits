using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public sealed partial class MsfxTaskSplit(DialogManager dialogManager)
    : FormBase(dialogManager)
{
    private static readonly MsfxTaskSplitResult CancelResult =
        new(MsfxTaskSplitAction.Cancel);

    private string _customQuantities = string.Empty;

    public required MsfxTaskSplitArgs Args { get; init; }

    public MsfxTaskSplitResult? Result { get; private set; }

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
        => Complete(MsfxTaskSplitAction.Batch);

    [RelayCommand]
    private void SplitCustom()
        => Complete(MsfxTaskSplitAction.CustomQuantity, CustomQuantities);

    [RelayCommand]
    private void SplitByParentCluster()
        => Complete(MsfxTaskSplitAction.ParentCluster);

    private void Complete(MsfxTaskSplitAction action, string? customQuantities = null)
    {
        Result = new MsfxTaskSplitResult(action, customQuantities);
        CloseDialog(success: action != MsfxTaskSplitAction.Cancel);
    }
}
