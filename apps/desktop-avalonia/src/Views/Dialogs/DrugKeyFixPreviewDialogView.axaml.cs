using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Views.Dialogs;

public sealed record DrugKeyFixPreviewDialogModel(
    string SourceKeyDisplay,
    string TargetKeyDisplay,
    string TracePoolAffectedDisplay,
    string TraceTxnAffectedDisplay,
    string TargetExistsDisplay);

public partial class DrugKeyFixPreviewDialogView : UserControl
{
    public DrugKeyFixPreviewDialogView()
    {
        InitializeComponent();
    }
}
