using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Dialogs;

public sealed class MsfxStateDetail(DialogManager dialogManager)
    : FormBase(dialogManager)
{
    public required MsfxStateDetailArgs Detail { get; init; }
}
