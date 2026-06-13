using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

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
    private DataGridColumn? _stockContextColumn;
    private StockRowItem? _stockContextRow;

    public InventoryOverviewView()
        : this(((global::Avalonia.Application.Current as App)?.Services.GetRequiredService<IClipboardService>())
               ?? throw new InvalidOperationException("IClipboardService not available. Ensure it is registered in App.Services."))
    {
    }

    public InventoryOverviewView(IClipboardService clipboard)
    {
        _clipboard = clipboard;
        InitializeComponent();
        AttachReassignDrugFilter();
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

    private void OnStockCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        try
        {
            if (DataContext is not InventoryOverviewViewModel vm)
                return;

            var row = e.Row?.DataContext as StockRowItem;
            _stockContextRow = row;
            _stockContextColumn = e.Column;

            if (sender is DataGrid grid && row is not null)
            {
                if (DataGridInteractionHelper.IsLeftClick(e.PointerPressedEventArgs, grid))
                {
                    // Keep current-cell aligned with the clicked cell to avoid "second click to focus".
                    Dispatcher.UIThread.Post(() =>
                    {
                        grid.SelectedItem = row;
                        grid.CurrentColumn = e.Column;
                    }, DispatcherPriority.Background);
                }
            }

            if (!vm.IsStockEditEnabled || !vm.IsDetailMode)
                return;

            var clickCount = e.PointerPressedEventArgs.ClickCount;
            if (clickCount >= 2 && e.Column.IsReadOnly)
            {
                vm.NotifyReadonlyStockColumnEditAttempt(e.Column.Header?.ToString());
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("InventoryOverviewView", "inventory.stock_cell_press.fail", "Failed handling stock cell pointer pressed", ex);
        }
    }

    private void OnStockBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        try
        {
            if (DataContext is not InventoryOverviewViewModel vm)
                return;

            if (!vm.IsStockEditEnabled || !vm.IsDetailMode)
                return;

            if (e.Column?.IsReadOnly != true)
                return;

            e.Cancel = true;
            vm.NotifyReadonlyStockColumnEditAttempt(e.Column?.Header?.ToString());
        }
        catch (Exception ex)
        {
            AppLog.Warn("InventoryOverviewView", "inventory.stock_begin_edit.fail", "Failed handling stock begin-edit", ex);
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
        _ = AutoCompleteHelper.HandleEnterCommitAndApply(
            this,
            sender,
            e,
            "ReassignSpecBox",
            () =>
            {
                if (DataContext is not InventoryOverviewViewModel vm)
                    return;
                if (vm.ApplyReassignDrugFilterCommand.CanExecute(null))
                    vm.ApplyReassignDrugFilterCommand.Execute(null);
            });
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
        var canShowEdit = vm.IsStockEditEnabled;
        var canEditCurrentCell = canShowEdit
                                 && _stockContextRow is not null
                                 && _stockContextColumn is not null
                                 && !_stockContextColumn.IsReadOnly;
        foreach (var item in cm.Items.OfType<MenuItem>())
        {
            if (string.Equals(item.Header?.ToString(), "删除", StringComparison.Ordinal))
                item.IsVisible = canShowDelete;
            else if (string.Equals(item.Header?.ToString(), "编辑", StringComparison.Ordinal))
            {
                item.IsVisible = canShowEdit;
                item.IsEnabled = canEditCurrentCell;
            }
        }
    }

    public void OnStockEditCell(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not InventoryOverviewViewModel vm || !vm.IsStockEditEnabled || !vm.IsDetailMode)
                return;

            var grid = this.FindControl<DataGrid>("StockDetailGrid");
            if (grid is null)
                return;

            var row = _stockContextRow ?? vm.SelectedStockRow;
            if (row is null)
                return;

            var column = _stockContextColumn ?? grid.Columns.FirstOrDefault(c => !c.IsReadOnly);
            if (column is null)
                return;

            if (column.IsReadOnly)
            {
                vm.NotifyReadonlyStockColumnEditAttempt(column.Header?.ToString());
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                grid.SelectedItem = row;
                grid.CurrentColumn = column;
                grid.ScrollIntoView(row, column);
                grid.Focus();
                _ = grid.BeginEdit(new RoutedEventArgs());
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            AppLog.Warn("InventoryOverviewView", "inventory.stock_context_edit.fail", "Failed opening stock cell editor from context menu", ex);
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

    private void AttachReassignDrugFilter()
    {
        if (this.FindControl<AutoCompleteBox>("ReassignDrugBox") is not { } box)
            return;
        AutoCompleteHelper.AttachDrugOptionFilter(box);
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
