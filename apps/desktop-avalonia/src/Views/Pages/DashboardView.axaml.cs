using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DashboardView : UserControl
{
    private static readonly string[] CopyFields =
    {
        "Title", "Detail", "DrugId", "Spec", "Time", "Qty", "ValueText", "SourceText", "Rank", "Message"
    };

    private static readonly string[] ContextGridNames =
    {
        "TrendGridOverview",
        "RecentTxnGridOverview",
        "EntryRecentGridInputTab",
        "TxnDetailGrid",
        "TxnTrendGrid",
        "AbnormalGrid"
    };
    private static readonly string[] BrowsingGridNames =
    {
        "TrendGridOverview",
        "RecentTxnGridOverview",
        "EntryRecentGridInputTab",
        "TxnDetailGrid",
        "TxnTrendGrid",
        "AbnormalGrid"
    };
    private static readonly string[] TargetTabGridNames =
    {
        "EntryRecentGridInputTab",
        "TxnDetailGrid",
        "TxnTrendGrid",
        "AbnormalGrid"
    };

    private bool _syncingSelection;
    private bool _vmHooked;
    private readonly IClipboardService _clipboard;
    private readonly PageGridMountScheduler _gridMount;
    private readonly Dictionary<string, List<object>> _selectionSnapshot = new(StringComparer.Ordinal);
    private string? _lastRightPressedGridName;

    public DashboardView()
        : this(((global::Avalonia.Application.Current as App)?.Services.GetRequiredService<IClipboardService>())
               ?? throw new InvalidOperationException("IClipboardService not available. Ensure it is registered in App.Services."))
    {
    }

    public DashboardView(IClipboardService clipboard)
    {
        _clipboard = clipboard;
        _gridMount = new PageGridMountScheduler(this);
        InitializeComponent();
        AttachDrugFilter();
        WireDeferredGridSlots();
        _gridMount.StartAfterFirstLayout();
        DataContextChanged += OnDashboardDataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gridMount.Cancel();
        if (DataContext is DashboardViewModel vm)
        {
            vm.PropertyChanged -= OnDashboardVmPropertyChanged;
        }

        _vmHooked = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void WireDeferredGridSlots()
    {
        WireOverviewSlot(TrendGridSlot);
        WireOverviewSlot(RecentTxnGridSlot);
        WireTargetSlot(EntryGridSlot);
        WireTargetSlot(TxnDetailGridSlot);
        WireTargetSlot(TxnTrendGridSlot);
        WireTargetSlot(AbnormalGridSlot);
    }

    private void WireOverviewSlot(DeferredGridSlot slot)
    {
        slot.GridMounted += (_, grid) => grid.SelectionChanged += OnBrowsingGridSelectionChanged;
    }

    private void WireTargetSlot(DeferredGridSlot slot)
    {
        slot.GridMounted += (_, grid) =>
        {
            grid.SelectionChanged += OnBrowsingGridSelectionChanged;
            grid.PointerPressed += OnBrowsingGridPointerPressed;
        };
    }

    private void OnDashboardDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            if (!_vmHooked)
            {
                vm.PropertyChanged += OnDashboardVmPropertyChanged;
                _vmHooked = true;
            }

            QueueTabGrids(vm);
            TryQueueOverviewGrids(vm);
        }
    }

    private void OnDashboardVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DashboardViewModel vm)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(DashboardViewModel.SelectedTabIndex):
                QueueTabGrids(vm);
                break;
            case nameof(DashboardViewModel.IsTrendEmpty):
                TryQueueOverviewGrids(vm);
                break;
            case nameof(DashboardViewModel.IsRecentTxnsEmpty):
                TryQueueOverviewGrids(vm);
                if (vm.IsTxnTab)
                {
                    QueueTabGrids(vm);
                }

                break;
            case nameof(DashboardViewModel.IsEntryRecentEmpty):
                if (vm.IsInputTab)
                {
                    QueueTabGrids(vm);
                }

                break;
            case nameof(DashboardViewModel.IsTxnTrendEmpty):
                if (vm.IsTxnTab)
                {
                    QueueTabGrids(vm);
                }

                break;
            case nameof(DashboardViewModel.IsAbnormalEmpty):
                if (vm.IsAbnormalTab)
                {
                    QueueTabGrids(vm);
                }

                break;
        }
    }

    private void TryQueueOverviewGrids(DashboardViewModel vm)
    {
        if (!vm.IsTrendEmpty && !TrendGridSlot.IsMounted)
        {
            _gridMount.RequestMount(TrendGridSlot, 0);
        }

        if (vm.RecentTxnsOverview.Count > 0 && !RecentTxnGridSlot.IsMounted)
        {
            _gridMount.RequestMount(RecentTxnGridSlot, 1);
        }
    }

    private void QueueTabGrids(DashboardViewModel vm)
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
        _ = AutoCompleteHelper.HandleEnterCommitAndApply(
            this,
            sender,
            e,
            "SpecBox",
            ApplyDrugFilterFromBox);
    }

    private async void OnBrowsingGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (_syncingSelection)
            {
                return;
            }

            if (sender is not DataGrid activeGrid)
            {
                return;
            }

            if (DataContext is not DashboardViewModel vm)
            {
                return;
            }

            CaptureSelectionSnapshot(activeGrid);

            if (!vm.IsOverviewTab)
            {
                return;
            }

            var selected = e.AddedItems.Count > 0 ? e.AddedItems[0] : activeGrid.SelectedItem;
            if (selected is null)
            {
                return;
            }

            switch (activeGrid.Name)
            {
                case "TrendGridOverview":
                    await vm.HandleTrendRowSelectedAsync(selected as TrendDrugItem);
                    break;
                case "RecentTxnGridOverview":
                    await vm.HandleRecentTxnRowSelectedAsync(selected as TxnItem);
                    break;
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

        AutoCompleteHelper.AttachDrugOptionFilter(box);
        AutoCompleteHelper.AttachCandidateCommitApply(box, this, "SpecBox", ApplyDrugFilterFromBox);
    }

    private void ApplyDrugFilterFromBox()
    {
        if (DataContext is not DashboardViewModel vm)
        {
            return;
        }

        var cmd = vm.ApplyDrugFilterCommand;
        if (cmd.CanExecute(null))
        {
            cmd.Execute(null);
        }
    }

    public async void OnGridRowCopy(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi)
        {
            return;
        }

        await GridContextMenuActions.CopySafeAsync(
            _clipboard,
            this,
            mi,
            mi.CommandParameter,
            ContextGridNames,
            CopyFields,
            "DashboardView",
            "dashboard.context_copy.fail",
            "Failed copying dashboard rows",
            (grid, selected) =>
            {
                if (selected.Count <= 1 &&
                    grid.Name is { Length: > 0 } key &&
                    _selectionSnapshot.TryGetValue(key, out var snap) &&
                    snap.Count > 1)
                {
                    return snap;
                }

                return selected;
            });
    }

    public void OnGridSelectAll(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi)
        {
            return;
        }

        GridContextMenuActions.SelectAllSafe(
            this,
            mi,
            mi.CommandParameter,
            ContextGridNames,
            "DashboardView",
            "dashboard.context_select_all.fail",
            "Failed selecting all rows",
            CaptureSelectionSnapshot);
    }

    private void OnBrowsingGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid || string.IsNullOrWhiteSpace(grid.Name))
        {
            return;
        }

        if (DataGridInteractionHelper.IsRightClick(e, grid))
        {
            _lastRightPressedGridName = grid.Name;
        }
    }

    private void CaptureSelectionSnapshot(DataGrid grid)
    {
        if (string.IsNullOrWhiteSpace(grid.Name))
        {
            return;
        }

        var name = grid.Name!;
        var selected = DataGridInteractionHelper.ReadSelectedItems(grid);

        if (selected.Count == 0)
        {
            _selectionSnapshot.Remove(name);
            return;
        }

        if (selected.Count == 1 && string.Equals(_lastRightPressedGridName, name, StringComparison.Ordinal))
        {
            return;
        }

        _selectionSnapshot[name] = selected;
    }

    private async void OnEntryRecentRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (DataContext is not DashboardViewModel vm)
            {
                return;
            }

            if (sender is not Border { DataContext: EntryRecentItem item })
            {
                return;
            }

            ClearBrowsingSelectionInUi(vm);
            await vm.HandleEntryRecentRowSelectedAsync(item);
        }
        catch (Exception ex)
        {
            AppLog.Warn("DashboardView", "dashboard.pointer_handler.fail", "Dashboard pointer handler failed", ex);
        }
    }

    private void ClearBrowsingSelectionInUi(DashboardViewModel vm)
    {
        _syncingSelection = true;
        vm.SuppressRowSelectionActionScope(true);
        try
        {
            foreach (var grid in GetBrowsingGrids())
            {
                grid.SelectedItem = null;
            }

            vm.ClearBrowsingSelections();
        }
        finally
        {
            vm.SuppressRowSelectionActionScope(false);
            _syncingSelection = false;
        }
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

    private void ClearGridSelection(DataGrid grid)
    {
        _syncingSelection = true;
        try
        {
            HardClearGridSelection(grid);

            if (DataContext is DashboardViewModel vm)
            {
                vm.SuppressRowSelectionActionScope(true);
                try
                {
                    switch (grid.Name)
                    {
                        case "EntryRecentGridInputTab":
                            vm.SelectedEntryRecent = null;
                            break;
                        case "TxnDetailGrid":
                            vm.SelectedTxn = null;
                            break;
                        case "TxnTrendGrid":
                            vm.SelectedTrendItem = null;
                            break;
                        case "AbnormalGrid":
                            vm.SelectedAbnormal = null;
                            break;
                    }
                }
                finally
                {
                    vm.SuppressRowSelectionActionScope(false);
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void ClearAllTargetTabGridSelections()
    {
        _syncingSelection = true;
        try
        {
            foreach (var name in TargetTabGridNames)
            {
                if (DataGridInteractionHelper.FindDeferredGrid(this, name) is { } grid)
                {
                    HardClearGridSelection(grid);
                }
            }

            if (DataContext is DashboardViewModel vm)
            {
                vm.SuppressRowSelectionActionScope(true);
                try
                {
                    vm.SelectedEntryRecent = null;
                    vm.SelectedTxn = null;
                    vm.SelectedTrendItem = null;
                    vm.SelectedAbnormal = null;
                }
                finally
                {
                    vm.SuppressRowSelectionActionScope(false);
                }
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private static void HardClearGridSelection(DataGrid grid)
    {
        DataGridInteractionHelper.ClearSelection(grid);
    }

}
