using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

/// <summary>
/// 通用信息详情对话框 ViewModel
/// </summary>
public sealed partial class InfoDetail(DialogManager dialogManager)
    : FormBase(dialogManager)
{
    public required InfoDetailArgs Detail { get; init; }

    [RelayCommand]
    private void Close()
        => CloseDialog(success: true);
}
