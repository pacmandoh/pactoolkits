using Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class MsfxAutoPullPanelView : UserControl
{
    public MsfxAutoPullPanelView()
    {
        InitializeComponent();
        MsfxAutoPanelGridInteraction.AttachDetailRowClick(
            AutoPullBatchGrid,
            allowRowBodyClick: true,
            (vm, row) => vm.ShowPullBatchDetailCommand.Execute(row));
    }
}
