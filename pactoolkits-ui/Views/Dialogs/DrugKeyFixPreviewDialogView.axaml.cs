using Avalonia.Controls;

namespace pactoolkits_ui.Views.Dialogs;

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
