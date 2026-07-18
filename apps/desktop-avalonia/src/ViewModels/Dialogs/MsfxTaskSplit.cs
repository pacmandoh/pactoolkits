using CommunityToolkit.Mvvm.ComponentModel;
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

    [ObservableProperty] private bool _isCustomSplitOpen;

    public string CustomQuantities
    {
        get => _customQuantities;
        set
        {
            if (SetProperty(ref _customQuantities, value))
            {
                ConfirmCustomSplitCommand.NotifyCanExecuteChanged();
            }
        }
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
    private void BeginCustomSplit()
        => IsCustomSplitOpen = true;

    [RelayCommand]
    private void CancelCustomSplit()
    {
        CustomQuantities = string.Empty;
        IsCustomSplitOpen = false;
    }

    private bool CanConfirmCustomSplit()
        => !string.IsNullOrWhiteSpace(CustomQuantities);

    [RelayCommand(CanExecute = nameof(CanConfirmCustomSplit))]
    private void ConfirmCustomSplit()
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
