using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using System.Linq;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

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
        if (e.Source is CheckBox or ToggleButton)
            return;
        if (DataContext is MsfxLinkViewModel { IsTaskQueueBatchModeActive: true }
            && grid.Name is "AutoTaskQueueGrid" or "AutoTaskQueueFullGrid")
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

    private void OnTaskQueueSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MsfxLinkViewModel { IsTaskQueueBatchModeActive: true })
            return;

        if (sender is DataGrid grid)
            grid.SelectedItem = null;
    }

    private void OnTaskQueueCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MsfxLinkViewModel vm)
            return;

        Dispatcher.UIThread.Post(vm.SyncCheckedAutoTaskQueueRows, DispatcherPriority.Background);
    }
}
