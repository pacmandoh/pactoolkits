using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using pactoolkits_ui.Common;
using pactoolkits_ui.Services;
using pactoolkits_ui.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace pactoolkits_ui.Views.Pages;

public partial class InventoryOverviewView : UserControl
{
    private static readonly string[] CopyFields =
    {
        "DrugId", "Spec", "TraceCode", "Qty", "Remain", "CodeCount", "QtySum", "RemainSum", "Threshold"
    };

    private static readonly string[] ContextGridNames =
    {
        "StockDetailGrid", "AggGrid", "LowStockGrid", "MissingGrid"
    };

    private readonly IClipboardService _clipboard;
    private InventoryOverviewViewModel? _vm;
    private bool _isSyncingSelectionFromVm;
    private bool _isSyncingSelectionToVm;

    public InventoryOverviewView()
        : this(((Application.Current as App)?.Services.GetRequiredService<IClipboardService>())
               ?? throw new InvalidOperationException("IClipboardService not available. Ensure it is registered in App.Services."))
    {
    }

    public InventoryOverviewView(IClipboardService clipboard)
    {
        _clipboard = clipboard;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private async void OnLowOrMissingGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (DataContext is not InventoryOverviewViewModel vm)
                return;

            var row = InputFocusHelper.FindAncestor<DataGridRow>(e.Source);
            if (TryExtractDrugSpec(row?.DataContext, out var drug, out var spec))
            {
                await vm.OpenScanCodeByRowAsync(drug, spec);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("InventoryOverviewView", "inventory.pointer_release.fail", "Failed handling row pointer action", ex);
        }
    }

    private async void OnStockCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        try
        {
            if (DataContext is not InventoryOverviewViewModel vm)
                return;

            if (e.Row.DataContext is not StockRowItem row)
                return;

            if (e.EditingElement is not TextBox editor)
                return;

            var header = e.Column?.Header?.ToString();
            await vm.CommitStockCellEditAsync(row, header, editor.Text);
        }
        catch (Exception ex)
        {
            AppLog.Warn("InventoryOverviewView", "inventory.stock_edit_end.fail", "Failed committing stock cell edit", ex);
        }
    }

    private void OnStockSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelectionFromVm || _isSyncingSelectionToVm)
            return;

        if (DataContext is not InventoryOverviewViewModel vm || sender is not DataGrid dg)
            return;

        var rows = dg.SelectedItems.OfType<StockRowItem>().ToArray();
        _isSyncingSelectionToVm = true;
        try
        {
            vm.SetSelectedStockRows(rows);
        }
        finally
        {
            _isSyncingSelectionToVm = false;
        }
    }

    private void ReassignDrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not AutoCompleteBox box)
            return;

        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        Dispatcher.UIThread.Post(() =>
        {
            InputFocusHelper.CommitAutoCompleteInput(box);

            if (DataContext is not InventoryOverviewViewModel vm)
                return;

            if (vm.ApplyReassignDrugFilterCommand.CanExecute(null))
                vm.ApplyReassignDrugFilterCommand.Execute(null);

            // Behave like tab-out: leave autocomplete and move to next field.
            InputFocusHelper.FocusControlByName(this, "ReassignSpecBox");
        }, DispatcherPriority.Input);
    }

    private void InventorySearchBox_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        InputFocusHelper.FocusControlByName(this, "InventorySearchButton", DispatcherPriority.Background);
    }

    public async void OnGridRowCopy(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        await GridContextMenuActions.CopySafeAsync(
            _clipboard,
            this,
            mi,
            mi.CommandParameter,
            ContextGridNames,
            CopyFields,
            "InventoryOverviewView",
            "inventory.context_copy.fail",
            "Failed copying selected rows");
    }

    public void OnGridSelectAll(object? sender, RoutedEventArgs e)
    {
        var mi = sender as MenuItem;
        GridContextMenuActions.SelectAllSafe(
            this,
            mi,
            mi?.CommandParameter,
            ContextGridNames,
            "InventoryOverviewView",
            "inventory.context_select_all.fail",
            "Failed selecting all rows");
    }

    public async void OnGridRowDelete(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not InventoryOverviewViewModel vm)
                return;

            if (sender is not MenuItem mi)
                return;

            await vm.DeleteSelectedStockRowsAsync(mi.CommandParameter as StockRowItem);
        }
        catch (Exception ex)
        {
            AppLog.Warn("InventoryOverviewView", "inventory.context_delete.fail", "Failed deleting selected rows", ex);
        }
    }

    public void OnStockDetailContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu cm)
            return;

        if (DataContext is not InventoryOverviewViewModel vm)
            return;

        vm.SyncUnlockStateForUi();
        var canShowDelete = vm.IsStockEditEnabled && vm.IsOperationUnlocked;
        foreach (var item in cm.Items.OfType<MenuItem>())
        {
            if (string.Equals(item.Header?.ToString(), "删除", StringComparison.Ordinal))
                item.IsVisible = canShowDelete;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as InventoryOverviewViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;

        SyncStockEditClass();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        OnDataContextChanged(this, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InventoryOverviewViewModel.IsStockEditEnabled))
            SyncStockEditClass();

        if (e.PropertyName == nameof(InventoryOverviewViewModel.SelectedStockRow))
            ScrollToSelectedStockRow();

        if (e.PropertyName == nameof(InventoryOverviewViewModel.SelectedStockRowsSnapshot) && !_isSyncingSelectionToVm)
            SyncSelectionFromVm();
    }

    private void SyncStockEditClass()
    {
        var grid = this.FindControl<DataGrid>("StockDetailGrid");
        if (grid is null)
            return;

        var editable = _vm?.IsStockEditEnabled == true;
        if (editable)
        {
            if (!grid.Classes.Contains("StockEditable"))
                grid.Classes.Add("StockEditable");
        }
        else
        {
            grid.Classes.Remove("StockEditable");
            grid.SelectedItem = null;
        }
    }

    private static bool TryExtractDrugSpec(object? rowData, out string drugId, out string spec)
    {
        switch (rowData)
        {
            case LowStockRowItem low:
                drugId = low.DrugId;
                spec = low.Spec;
                return true;
            case MissingStockRowItem missing:
                drugId = missing.DrugId;
                spec = missing.Spec;
                return true;
            default:
                drugId = string.Empty;
                spec = string.Empty;
                return false;
        }
    }

    private void ScrollToSelectedStockRow()
    {
        var selected = _vm?.SelectedStockRow;
        if (selected is null)
            return;

        Dispatcher.UIThread.Post(() => EnsureGridSelectionAndScroll(selected), DispatcherPriority.Background);
    }

    private void EnsureGridSelectionAndScroll(StockRowItem selected)
    {
        var grid = this.FindControl<DataGrid>("StockDetailGrid");
        if (grid is null)
            return;

        grid.ScrollIntoView(selected, null);
    }

    private void SyncSelectionFromVm()
    {
        var grid = this.FindControl<DataGrid>("StockDetailGrid");
        if (grid is null || _vm is null)
            return;

        if (IsSelectionAlreadySynced(grid, _vm.SelectedStockRowsSnapshot))
            return;

        _isSyncingSelectionFromVm = true;
        try
        {
            grid.SelectedItems.Clear();
            foreach (var row in _vm.SelectedStockRowsSnapshot)
                grid.SelectedItems.Add(row);
        }
        finally
        {
            _isSyncingSelectionFromVm = false;
        }
    }

    private static bool IsSelectionAlreadySynced(DataGrid grid, IReadOnlyList<StockRowItem> snapshot)
    {
        if (grid.SelectedItems.Count != snapshot.Count)
            return false;

        for (var i = 0; i < snapshot.Count; i++)
        {
            if (!ReferenceEquals(grid.SelectedItems[i], snapshot[i]))
                return false;
        }

        return true;
    }

}
