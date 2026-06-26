using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public sealed partial class DrugKeyFixPreviewDialogViewModel(DialogManager dialogManager)
    : FormDialogViewModelBase(dialogManager)
{
    public required DrugKeyFixPreviewDialogModel Preview { get; init; }

    [RelayCommand]
    private void Confirm()
        => CloseDialog(success: true);

    [RelayCommand]
    private void Cancel()
        => CloseDialog();
}
