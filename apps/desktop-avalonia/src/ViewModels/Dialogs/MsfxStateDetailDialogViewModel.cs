using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public sealed partial class MsfxStateDetailDialogViewModel(DialogManager dialogManager)
    : FormDialogViewModelBase(dialogManager)
{
    public required MsfxStateDetailDialogModel Detail { get; init; }

    [RelayCommand]
    private void Close()
        => CloseDialog(success: true);
}
