using Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class MsfxLinkRun : UserControl
{
    public MsfxLinkRun()
    {
        InitializeComponent();
        MsfxAutoPanelGridInteraction.AttachDetailRowClick(
            AutoPullBatchGrid,
            allowRowBodyClick: true,
            (vm, row) => vm.ShowPullBatchDetailCommand.Execute(row));
        MsfxAutoPanelGridInteraction.AttachDetailRowClick(
            AutoLogGrid,
            allowRowBodyClick: true,
            (vm, row) => vm.ShowAutoLogDetailCommand.Execute(row));
    }
}
