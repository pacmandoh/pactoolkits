using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DashboardView : UserControl
{
    private static readonly string[] BrowsingGridNames =
    {
        "TrendGridOverview",
        "RecentTxnGridOverview",
        "TopClientsGridOverview",
        "EntryRecentGridInputTab",
        "TxnDetailGrid",
        "TxnTrendGrid",
        "AbnormalGrid"
    };

    private bool _syncingSelection;
    private bool _vmHooked;
    private readonly PageGridMountScheduler _gridMount;
    private readonly DashboardGridActivation _gridActivation;

    public DashboardView()
    {
        _gridMount = new PageGridMountScheduler(this);
        _gridActivation = new DashboardGridActivation(OpenOverviewItemAsync);
        InitializeComponent();
        AttachDrugFilter();
        WireOverviewGridSlots();
        _gridMount.StartAfterFirstLayout();
        DataContextChanged += OnDashboardDataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gridMount.Cancel();
        if (DataContext is Dashboard vm)
        {
            vm.PropertyChanged -= OnDashboardVmPropertyChanged;
        }

        _vmHooked = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void WireOverviewGridSlots()
    {
        WireOverviewSlot(TrendGridSlot);
        WireOverviewSlot(RecentTxnGridSlot);
        WireOverviewSlot(TopClientsGridSlot);
    }

    private void WireOverviewSlot(DeferredGridSlot slot)
    {
        slot.GridMounted += (_, grid) => _gridActivation.Attach(grid);
    }

    private void OnDashboardDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is Dashboard vm)
        {
            if (!_vmHooked)
            {
                vm.PropertyChanged += OnDashboardVmPropertyChanged;
                _vmHooked = true;
            }

            QueueTabGrids(vm);
            QueueOverviewGrids(vm);
            MountTrendChart(vm);
        }
    }

    private void OnDashboardVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Dashboard vm)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(Dashboard.SelectedTabIndex):
                QueueTabGrids(vm);
                break;
            case nameof(Dashboard.IsTrendEmpty):
                QueueOverviewGrids(vm);
                break;
            case nameof(Dashboard.IsTrendChartVisible):
                MountTrendChart(vm);
                break;
            case nameof(Dashboard.IsRecentTxnsEmpty):
                QueueOverviewGrids(vm);
                if (vm.IsTxnTab)
                {
                    QueueTabGrids(vm);
                }

                break;
            case nameof(Dashboard.IsTopClientsEmpty):
                QueueOverviewGrids(vm);
                break;
            case nameof(Dashboard.IsEntryRecentEmpty):
                if (vm.IsInputTab)
                {
                    QueueTabGrids(vm);
                }

                break;
            case nameof(Dashboard.IsTxnTrendEmpty):
                if (vm.IsTxnTab)
                {
                    QueueTabGrids(vm);
                }

                break;
            case nameof(Dashboard.IsAbnormalEmpty):
                if (vm.IsAbnormalTab)
                {
                    QueueTabGrids(vm);
                }

                break;
        }
    }

    private void QueueOverviewGrids(Dashboard vm)
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

    private void MountTrendChart(Dashboard vm)
    {
        if (!vm.IsTrendChartVisible || TrendChartHost.Content is DrugTrendChart)
        {
            return;
        }

        TrendChartHost.Content = new DrugTrendChart
        {
            ItemsSource = vm.DrugTrend
        };
    }

    private void QueueTabGrids(Dashboard vm)
    {
        switch (vm.SelectedTabIndex)
        {
            case 1:
                if (!vm.IsEntryRecentEmpty && !EntryGridSlot.IsMounted)
                {
                    _gridMount.RequestMount(EntryGridSlot, 10);
                }

                break;
            case 2:
                if (!vm.IsRecentTxnsEmpty && !TxnDetailGridSlot.IsMounted)
                {
                    _gridMount.RequestMount(TxnDetailGridSlot, 10);
                }

                if (!vm.IsTxnTrendEmpty && !TxnTrendGridSlot.IsMounted)
                {
                    _gridMount.RequestMount(TxnTrendGridSlot, 11);
                }

                break;
            case 3:
                if (!vm.IsAbnormalEmpty && !AbnormalGridSlot.IsMounted)
                {
                    _gridMount.RequestMount(AbnormalGridSlot, 10);
                }

                break;
        }
    }

    private void DrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteCommit.CommitOnEnter(
            this,
            sender,
            e,
            "SpecBox",
            ApplyDrugFilterFromBox);
    }

    private async Task OpenOverviewItemAsync(DataGrid activeGrid, object selected)
    {
        try
        {
            if (_syncingSelection)
            {
                return;
            }

            if (DataContext is not Dashboard vm)
            {
                return;
            }

            if (!vm.IsOverviewTab)
            {
                return;
            }

            _syncingSelection = true;
            try
            {
                ClearBrowsingSelectionInUi(vm);

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
            AppLog.Warn("DashboardView", "dashboard.selection_handler.fail", "Dashboard selection handler failed", ex);
        }
    }

    private void AttachDrugFilter()
    {
        if (this.FindControl<AutoCompleteBox>("DrugBox") is not { } box)
        {
            return;
        }

        AutoCompleteFilter.AttachDrugOptionFilter(box);
        AutoCompleteCommit.AttachCandidateCommitApply(box, this, "SpecBox", ApplyDrugFilterFromBox);
    }

    private void ApplyDrugFilterFromBox()
    {
        if (DataContext is not Dashboard vm)
        {
            return;
        }

        var cmd = vm.ApplyDrugFilterCommand;
        if (cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    private async void OnEntryRecentRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (_syncingSelection)
            {
                return;
            }

            if (DataContext is not Dashboard vm)
            {
                return;
            }

            if (sender is not Border { DataContext: EntryRecentItem item })
            {
                return;
            }

            _syncingSelection = true;
            try
            {
                ClearBrowsingSelectionInUi(vm);
                await vm.OpenEntryAsync(item);
            }
            finally
            {
                _syncingSelection = false;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("DashboardView", "dashboard.pointer_handler.fail", "Dashboard pointer handler failed", ex);
        }
    }

    private void ClearBrowsingSelectionInUi(Dashboard vm)
    {
        foreach (var grid in GetBrowsingGrids())
        {
            HardClearGridSelection(grid);
        }

        vm.ClearBrowsingSelections();
    }

    private IEnumerable<DataGrid> GetBrowsingGrids()
    {
        foreach (var name in BrowsingGridNames)
        {
            if (DataGridInteractionHelper.FindDeferredGrid(this, name) is { } grid)
            {
                yield return grid;
            }
        }
    }

    private static void HardClearGridSelection(DataGrid grid)
    {
        DataGridInteractionHelper.ClearSelection(grid);
    }

}
