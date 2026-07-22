using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Dialogs;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

/// <summary>
/// MSFX 状态详情只读对话框 ViewModel
/// </summary>
public sealed class MsfxStateDetail(DialogManager dialogManager)
    : FormBase(dialogManager)
{
    public required MsfxStateDetailArgs Detail { get; init; }
}
