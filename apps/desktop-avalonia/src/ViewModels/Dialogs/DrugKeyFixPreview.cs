using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

/// <summary>
/// 药品主键修复预览确认对话框 ViewModel
/// </summary>
public sealed partial class DrugKeyFixPreview(DialogManager dialogManager)
    : FormBase(dialogManager)
{
    public required DrugKeyFixPreviewArgs Preview { get; init; }

    [RelayCommand]
    private void Confirm()
        => CloseDialog(success: true);

    [RelayCommand]
    private void Cancel()
        => CloseDialog();
}
