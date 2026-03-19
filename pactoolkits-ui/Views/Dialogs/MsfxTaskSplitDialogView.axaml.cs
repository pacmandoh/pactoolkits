using Avalonia.Controls;

namespace pactoolkits_ui.Views.Dialogs;

public partial class MsfxTaskSplitDialogView : UserControl
{
    public MsfxTaskSplitDialogView()
    {
        InitializeComponent();
    }

    public string CustomQuantities => QuantitySplitBox?.Text ?? string.Empty;
}
