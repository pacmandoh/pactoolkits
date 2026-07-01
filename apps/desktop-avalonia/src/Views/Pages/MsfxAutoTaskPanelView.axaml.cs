using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class MsfxAutoTaskPanelView : UserControl
{
    private MsfxLink? _viewModel;

    public MsfxAutoTaskPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnPanelDataContextChanged;
        AutoTaskQueueGrid.Initialized += OnTaskQueueGridInitialized;
        MsfxAutoPanelGridInteraction.AttachDetailRowClick(
            AutoTaskQueueGrid,
            allowRowBodyClick: false,
            (vm, row) => vm.ShowTaskQueueDetailCommand.Execute(row),
            vm => vm.IsTaskQueueBatchModeActive);
    }

    private void OnTaskQueueGridInitialized(object? sender, EventArgs e)
        => SyncBatchCheckColumnVisibility();

    private void OnPanelDataContextChanged(object? sender, EventArgs e)
    {
        _viewModel?.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as MsfxLink;
        _viewModel?.PropertyChanged += OnViewModelPropertyChanged;
        SyncBatchCheckColumnVisibility();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MsfxLink.IsTaskQueueBatchModeActive))
        {
            SyncBatchCheckColumnVisibility();
        }
    }

    private void SyncBatchCheckColumnVisibility()
    {
        var visible = _viewModel?.IsTaskQueueBatchModeActive == true;
        foreach (var column in AutoTaskQueueGrid.Columns)
        {
            if (column is DataGridTemplateColumn { Tag: "BatchCheck" } batchColumn)
            {
                batchColumn.IsVisible = visible;
                return;
            }
        }
    }

    private void OnTaskQueueSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MsfxLink { IsTaskQueueBatchModeActive: true })
        {
            return;
        }

        if (sender is DataGrid grid)
        {
            grid.SelectedItem = null;
        }
    }

    private void OnTaskQueueCheckChanged(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MsfxLink vm)
        {
            return;
        }

        Dispatcher.UIThread.Post(vm.SyncCheckedAutoTaskQueueRows, DispatcherPriority.Background);
    }
}
