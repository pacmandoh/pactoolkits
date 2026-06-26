using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public sealed partial class InfoDetailDialogViewModel(DialogManager dialogManager)
    : FormDialogViewModelBase(dialogManager)
{
    public required InfoDetailDialogModel Detail { get; init; }

    [RelayCommand]
    private void Close()
        => CloseDialog(success: true);
}
