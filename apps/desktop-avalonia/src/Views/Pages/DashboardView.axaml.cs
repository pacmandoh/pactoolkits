using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

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
    private readonly IClipboardService _clipboard;
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
        InitializeComponent();
        AttachDrugFilter();
    }

    private void DrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteHelper.HandleEnterCommitAndApply(
            this,
            sender,
            e,
            "SpecBox",
            () =>
            {
                if (DataContext is not PacToolkits.Desktop.Avalonia.ViewModels.Pages.DashboardViewModel vm)
                {
                    return;
                }

                var cmd = vm.ApplyDrugFilterCommand;
                if (cmd.CanExecute(null))
                {
                    cmd.Execute(null);
                }
            });
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

            if (DataContext is not PacToolkits.Desktop.Avalonia.ViewModels.Pages.DashboardViewModel vm)
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
                    await vm.HandleTrendRowSelectedAsync(selected as PacToolkits.Desktop.Avalonia.ViewModels.Pages.TrendDrugItem);
                    break;
                case "RecentTxnGridOverview":
                    await vm.HandleRecentTxnRowSelectedAsync(selected as PacToolkits.Desktop.Avalonia.ViewModels.Pages.TxnItem);
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
            if (DataContext is not PacToolkits.Desktop.Avalonia.ViewModels.Pages.DashboardViewModel vm)
            {
                return;
            }

            if (sender is not Border { DataContext: PacToolkits.Desktop.Avalonia.ViewModels.Pages.EntryRecentItem item })
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

    private void ClearBrowsingSelectionInUi(PacToolkits.Desktop.Avalonia.ViewModels.Pages.DashboardViewModel vm)
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
            if (this.FindControl<DataGrid>(name) is { } grid)
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

            if (DataContext is PacToolkits.Desktop.Avalonia.ViewModels.Pages.DashboardViewModel vm)
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
                if (this.FindControl<DataGrid>(name) is { } grid)
                {
                    HardClearGridSelection(grid);
                }
            }

            if (DataContext is PacToolkits.Desktop.Avalonia.ViewModels.Pages.DashboardViewModel vm)
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
