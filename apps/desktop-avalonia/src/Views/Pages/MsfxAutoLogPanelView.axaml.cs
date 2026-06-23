using Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class MsfxAutoLogPanelView : UserControl
{
    public MsfxAutoLogPanelView()
    {
        InitializeComponent();
        MsfxAutoPanelGridInteraction.AttachDetailRowClick(
            AutoLogGrid,
            allowRowBodyClick: true,
            (vm, row) => vm.ShowAutoLogDetailCommand.Execute(row));
    }
}
