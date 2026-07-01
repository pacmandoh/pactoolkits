using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public sealed partial class InfoDetail(DialogManager dialogManager)
    : FormBase(dialogManager)
{
    public required InfoDetailArgs Detail { get; init; }

    [RelayCommand]
    private void Close()
        => CloseDialog(success: true);
}
