using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using DashboardViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.Dashboard;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DashboardOverview : UserControl
{
    private readonly DashboardGridActivation _gridActivation;
    private readonly PageGridMountScheduler _gridMount;
    private DashboardViewModel? _vm;
    private bool _syncingSelection;

    public DashboardOverview()
    {
        _gridMount = new PageGridMountScheduler(this);
        _gridActivation = new DashboardGridActivation(OpenOverviewItemAsync);
        InitializeComponent();
        ConfigureChartHosts();
        WireOverviewSlot(TrendGridSlot);
        WireOverviewSlot(RecentTxnGridSlot);
        WireOverviewSlot(TopClientsGridSlot);
        DataContextChanged += OnDataContextChanged;
        _gridMount.StartAfterFirstLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gridMount.Cancel();
        SetViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void WireOverviewSlot(DeferredGridSlot slot)
        => slot.GridMounted += (_, grid) => _gridActivation.Attach(grid);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        SetViewModel(DataContext as DashboardViewModel);
        if (_vm is not null)
        {
            QueueGrids(_vm);
            UpdateChartSelection(_vm.SelectedClient?.Raw);
        }
    }

    private void SetViewModel(DashboardViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm))
        {
            return;
        }

        _vm?.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = vm;
        _vm?.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DashboardViewModel vm)
        {
            return;
        }

        if (e.PropertyName is nameof(DashboardViewModel.IsTrendEmpty)
            or nameof(DashboardViewModel.IsRecentTxnsEmpty)
            or nameof(DashboardViewModel.IsTopClientsEmpty))
        {
            QueueGrids(vm);
        }
        else if (e.PropertyName == nameof(DashboardViewModel.SelectedClient))
        {
            UpdateChartSelection(vm.SelectedClient?.Raw);
        }
    }

    private void QueueGrids(DashboardViewModel vm)
    {
        if (!vm.IsTrendEmpty && !TrendGridSlot.IsMounted)
        {
            _gridMount.RequestMount(TrendGridSlot, 0);
        }

        if (vm.RecentTxnsOverview.Count > 0 && !RecentTxnGridSlot.IsMounted)
        {
            _gridMount.RequestMount(RecentTxnGridSlot, 1);
        }

        if (!vm.IsTopClientsEmpty && !TopClientsGridSlot.IsMounted)
        {
            _gridMount.RequestMount(TopClientsGridSlot, 2);
        }
    }

    private void ConfigureChartHosts()
    {
        TrendChartHost.Factory = () => DataContext is DashboardViewModel vm
            ? new DrugTrendChart { ItemsSource = vm.ChartDrugTrend }
            : null;
        TxnChartHost.Factory = () => DataContext is DashboardViewModel vm ? new TxnChart(vm.ChartTxns) : null;
        ClientChartHost.Factory = () => DataContext is DashboardViewModel vm
            ? new ClientChart(vm.ChartClients, vm.SelectedClient?.Raw)
            : null;
        EntryChartHost.Factory = () => DataContext is DashboardViewModel vm
            ? new EntryChart(vm.EntryChartRows, vm.SelectedClient?.Raw)
            : null;
    }

    private void UpdateChartSelection(string? selectedClient)
    {
        (ClientChartHost.Content as ClientChart)?.SetSelectedClient(selectedClient);
        (EntryChartHost.Content as EntryChart)?.SetSelectedClient(selectedClient);
    }

    private void OnTrendZoomInClicked(object? sender, RoutedEventArgs e)
        => (TrendChartHost.Content as DrugTrendChart)?.ZoomIn();

    private void OnTrendZoomOutClicked(object? sender, RoutedEventArgs e)
        => (TrendChartHost.Content as DrugTrendChart)?.ZoomOut();

    private void OnTxnZoomInClicked(object? sender, RoutedEventArgs e)
        => (TxnChartHost.Content as TxnChart)?.ZoomIn();

    private void OnTxnZoomOutClicked(object? sender, RoutedEventArgs e)
        => (TxnChartHost.Content as TxnChart)?.ZoomOut();

    private async Task OpenOverviewItemAsync(DataGrid activeGrid, object selected)
    {
        try
        {
            if (_syncingSelection || DataContext is not DashboardViewModel vm || !vm.IsOverviewTab)
            {
                return;
            }

            _syncingSelection = true;
            try
            {
                ClearBrowsingSelection(vm);
                switch (activeGrid.Name)
                {
                    case "TrendGridOverview":
                        await vm.OpenTrendDrugAsync(selected as TrendDrugItem);
                        break;
                    case "RecentTxnGridOverview":
                        await vm.OpenTxnAsync(selected as TxnItem);
                        break;
                    case "TopClientsGridOverview":
                        await vm.OpenClientAsync(selected as TopClientItem);
                        break;
                }
            }
            finally
            {
                _syncingSelection = false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Dashboard", "dashboard.selection_handler.fail", "Dashboard selection handler failed", ex);
        }
    }

    private async void OnEntryRecentRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (_syncingSelection
                || DataContext is not DashboardViewModel vm
                || sender is not Border { DataContext: EntryRecentItem item })
            {
                return;
            }

            _syncingSelection = true;
            try
            {
                ClearBrowsingSelection(vm);
                await vm.OpenEntryAsync(item);
            }
            finally
            {
                _syncingSelection = false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Dashboard", "dashboard.pointer_handler.fail", "Dashboard pointer handler failed", ex);
        }
    }

    private void ClearBrowsingSelection(DashboardViewModel vm)
    {
        foreach (var name in new[] { "TrendGridOverview", "RecentTxnGridOverview", "TopClientsGridOverview" })
        {
            if (DataGridInteractionHelper.FindDeferredGrid(this, name) is { } grid)
            {
                DataGridInteractionHelper.ClearSelection(grid);
            }
        }

        vm.ClearBrowsingSelections();
    }
}
