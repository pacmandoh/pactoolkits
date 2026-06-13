using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Views.Dialogs;

public partial class MsfxTaskSplitDialogView : UserControl
{
    public MsfxTaskSplitDialogView()
    {
        InitializeComponent();
    }

    public string CustomQuantities => QuantitySplitBox?.Text ?? string.Empty;
}
