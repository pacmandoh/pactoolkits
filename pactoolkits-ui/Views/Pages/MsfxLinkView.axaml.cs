using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using pactoolkits_ui.Common;
using pactoolkits_ui.ViewModels.Pages;

namespace pactoolkits_ui.Views.Pages;

public partial class MsfxLinkView : UserControl
{
    public MsfxLinkView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachGridRowHeaderClick("AutoPullBatchGrid");
        AttachGridRowHeaderClick("AutoMapQueueGrid");
        AttachGridRowHeaderClick("AutoTaskQueueGrid");
        AttachGridRowHeaderClick("AutoLogGrid");
        AttachGridRowHeaderClick("AutoPullBatchFullGrid");
        AttachGridRowHeaderClick("MapQueueFullGrid");
        AttachGridRowHeaderClick("AutoTaskQueueFullGrid");
        AttachGridRowHeaderClick("AutoLogFullGrid");
    }

    private void AttachGridRowHeaderClick(string gridName)
    {
        var grid = this.FindControl<DataGrid>(gridName);
        if (grid is null)
            return;

        grid.RemoveHandler(InputElement.PointerPressedEvent, OnAutoGridPointerPressed);
        grid.AddHandler(InputElement.PointerPressedEvent, OnAutoGridPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnAutoGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid)
            return;
        var isRowClickDetailGrid = grid.Name is
            "AutoLogGrid" or "AutoLogFullGrid" or
            "AutoPullBatchGrid" or "AutoPullBatchFullGrid";
        if (!DataGridInteractionHelper.TrySelectRowFromPointer(
                grid,
                e.Source,
                requireRowHeader: !isRowClickDetailGrid,
                out var rowData,
                out _))
            return;

        if (DataContext is not MsfxLinkViewModel vm)
            return;

        switch (grid.Name)
        {
            case "AutoPullBatchGrid":
            case "AutoPullBatchFullGrid":
                vm.ShowPullBatchDetailCommand.Execute(rowData);
                break;
            case "AutoMapQueueGrid":
            case "MapQueueFullGrid":
                vm.ShowMapQueueDetailCommand.Execute(rowData);
                break;
            case "AutoTaskQueueGrid":
            case "AutoTaskQueueFullGrid":
                vm.ShowTaskQueueDetailCommand.Execute(rowData);
                break;
            case "AutoLogGrid":
            case "AutoLogFullGrid":
                vm.ShowAutoLogDetailCommand.Execute(rowData);
                break;
        }
    }
}
