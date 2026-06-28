using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class InventoryOverviewView : UserControl
{
    private readonly PageGridMountScheduler _gridMount;
    private InventoryOverviewViewModel? _vm;
    private DeferredGridSlot? _reassignPreviewSlot;
    private bool _isSyncingSelectionFromVm;
    private bool _isSyncingSelectionToVm;

    public InventoryOverviewView()
    {
        _gridMount = new PageGridMountScheduler(this);
        InitializeComponent();
        ReassignPanelHost.ContentLoaded += OnReassignPanelContentLoaded;
        WireStockDetailGridSlot();
        _gridMount.StartAfterFirstLayout();
        DataContextChanged += OnDataContextChanged;

        LowStockGridSlot.GridMounted += (_, grid) => grid.PointerReleased += OnLowOrMissingGridPointerReleased;
        MissingGridSlot.GridMounted += (_, grid) => grid.PointerReleased += OnLowOrMissingGridPointerReleased;
    }

    private void OnReassignPanelContentLoaded(object? sender, Control root)
    {
        _reassignPreviewSlot ??= root.FindControl<DeferredGridSlot>("ReassignPreviewGridSlot");
        AttachReassignDrugFilter();
        QueueReassignGridMount();
    }

    private void QueueAggGridMount()
    {
        if (_vm?.IsAggMode == true && !_vm.IsAggEmpty && !AggGridSlot.IsMounted)
        {
            _gridMount.RequestMount(AggGridSlot, 2);
        }
    }

    private void QueueLowGridMount()
    {
        if (_vm?.IsLowMode == true && !_vm.IsLowEmpty && !LowStockGridSlot.IsMounted)
        {
            _gridMount.RequestMount(LowStockGridSlot, 3);
        }
    }

    private void QueueMissingGridMount()
    {
        if (_vm?.IsMissingMode == true && !_vm.IsMissingEmpty && !MissingGridSlot.IsMounted)
        {
            _gridMount.RequestMount(MissingGridSlot, 4);
        }
    }

    private void TryQueueActiveModeGrid()
    {
        if (_vm is null)
        {
            return;
        }

        if (_vm.IsAggMode)
        {
            QueueAggGridMount();
        }
        else if (_vm.IsLowMode)
        {
            QueueLowGridMount();
        }
        else if (_vm.IsMissingMode)
        {
            QueueMissingGridMount();
        }
    }

    private void WireStockDetailGridSlot()
    {
        StockDetailGridSlot.GridMounted += (_, grid) =>
        {
            grid.BeginningEdit += OnStockBeginningEdit;
            grid.CellEditEnding += OnStockCellEditEnding;
            grid.CellPointerPressed += OnStockCellPointerPressed;
            grid.SelectionChanged += OnStockSelectionChanged;
            SyncStockEditClass();
            ClearStockGridSelectionAfterRefresh();
        };
    }

    private void QueueStockDetailGridMount()
    {
        if (_vm?.IsDetailMode == true && !_vm.IsStockEmpty && !StockDetailGridSlot.IsMounted)
        {
            _gridMount.RequestMount(StockDetailGridSlot, 0);
        }
    }

    private void QueueReassignGridMount()
    {
        if (_reassignPreviewSlot is null
            || _vm?.IsReassignPanelVisible != true
            || _vm.IsReassignPreviewEmpty
            || _reassignPreviewSlot.IsMounted)
        {
            return;
        }

        _gridMount.RequestMount(_reassignPreviewSlot, 5);
    }

    private async void OnLowOrMissingGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (DataContext is not InventoryOverviewViewModel vm)
            {
                return;
            }

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
            {
                return;
            }

            if (e.Row.DataContext is not StockRowItem row)
            {
                return;
            }

            if (e.EditingElement is not TextBox editor)
            {
                return;
            }

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
            {
                return;
            }

            var row = e.Row?.DataContext as StockRowItem;

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
            {
                return;
            }

            var clickCount = e.PointerPressedEventArgs.ClickCount;
            if (clickCount >= 2 && e.Column.IsReadOnly)
            {
                vm.WarnReadonlyStockEdit(e.Column.Header?.ToString());
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
            {
                return;
            }

            if (!vm.IsStockEditEnabled || !vm.IsDetailMode)
            {
                return;
            }

            if (e.Column?.IsReadOnly != true)
            {
                return;
            }

            e.Cancel = true;
            vm.WarnReadonlyStockEdit(e.Column?.Header?.ToString());
        }
        catch (Exception ex)
        {
            AppLog.Warn("InventoryOverviewView", "inventory.stock_begin_edit.fail", "Failed handling stock begin-edit", ex);
        }
    }

    private void OnStockSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelectionFromVm || _isSyncingSelectionToVm)
        {
            return;
        }

        if (DataContext is not InventoryOverviewViewModel vm || sender is not DataGrid dg)
        {
            return;
        }

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
        _ = AutoCompleteCommit.CommitOnEnter(
            this,
            sender,
            e,
            "ReassignSpecBox",
            ApplyReassignDrugFilterFromBox);
    }

    private void ApplyReassignDrugFilterFromBox()
    {
        if (DataContext is not InventoryOverviewViewModel vm)
        {
            return;
        }

        if (vm.ApplyReassignDrugFilterCommand.CanExecute(null))
        {
            vm.ApplyReassignDrugFilterCommand.Execute(null);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _vm?.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as InventoryOverviewViewModel;
        _vm?.PropertyChanged += OnVmPropertyChanged;

        SyncStockEditClass();
        QueueStockDetailGridMount();
        QueueReassignGridMount();
        TryQueueActiveModeGrid();
    }

    private void AttachReassignDrugFilter()
    {
        if (this.FindControl<AutoCompleteBox>("ReassignDrugBox") is not { } box)
        {
            return;
        }

        AutoCompleteFilter.AttachDrugOptionFilter(box);
        AutoCompleteCommit.AttachCandidateCommitApply(box, this, "ReassignSpecBox", ApplyReassignDrugFilterFromBox);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        OnDataContextChanged(this, EventArgs.Empty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gridMount.Cancel();
        _vm?.PropertyChanged -= OnVmPropertyChanged;

        _vm = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnVmPropertyChanged(sender, e));
            return;
        }

        if (e.PropertyName == nameof(InventoryOverviewViewModel.IsStockEditEnabled))
        {
            SyncStockEditClass();
        }

        if (e.PropertyName == nameof(InventoryOverviewViewModel.ModeIndex))
        {
            QueueStockDetailGridMount();
            TryQueueActiveModeGrid();
        }

        if (e.PropertyName == nameof(InventoryOverviewViewModel.IsStockEmpty))
        {
            QueueStockDetailGridMount();
        }

        if (e.PropertyName is nameof(InventoryOverviewViewModel.IsAggEmpty)
            or nameof(InventoryOverviewViewModel.IsLowEmpty)
            or nameof(InventoryOverviewViewModel.IsMissingEmpty))
        {
            TryQueueActiveModeGrid();
        }

        if (e.PropertyName is nameof(InventoryOverviewViewModel.IsReassignPanelVisible)
            or nameof(InventoryOverviewViewModel.IsReassignPreviewEmpty))
        {
            QueueReassignGridMount();
        }

        if (e.PropertyName == nameof(InventoryOverviewViewModel.SelectedStockRow))
        {
            ScrollToSelectedStockRow();
        }

        if (e.PropertyName == nameof(InventoryOverviewViewModel.SelectedStockRowsSnapshot) && !_isSyncingSelectionToVm)
        {
            SyncSelectionFromVm();
        }

        if (e.PropertyName == nameof(InventoryOverviewViewModel.StockRowsRevision))
        {
            ClearStockGridSelectionAfterRefresh();
        }
    }

    private void ClearStockGridSelectionAfterRefresh()
    {
        var grid = FindStockDetailGrid();
        if (grid is null)
        {
            return;
        }

        ClearStockGridSelection(grid);
        Dispatcher.UIThread.Post(() => ClearStockGridSelection(grid), DispatcherPriority.Loaded);
    }

    private void ClearStockGridSelection(DataGrid grid)
    {
        _isSyncingSelectionFromVm = true;
        try
        {
            DataGridInteractionHelper.ClearSelection(grid);
        }
        finally
        {
            _isSyncingSelectionFromVm = false;
        }
    }

    private DataGrid? FindStockDetailGrid()
        => DataGridInteractionHelper.FindDeferredGrid(this, "StockDetailGrid");

    private void SyncStockEditClass()
    {
        var grid = FindStockDetailGrid();
        if (grid is null)
        {
            return;
        }

        var editable = _vm?.IsStockEditEnabled == true;
        if (editable)
        {
            if (!grid.Classes.Contains("StockEditable"))
            {
                grid.Classes.Add("StockEditable");
            }
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
        {
            return;
        }

        Dispatcher.UIThread.Post(() => SelectAndScrollRow(selected), DispatcherPriority.Background);
    }

    private void SelectAndScrollRow(StockRowItem selected)
    {
        var grid = FindStockDetailGrid();
        if (grid is null)
        {
            return;
        }

        grid.ScrollIntoView(selected, null);
    }

    private void SyncSelectionFromVm()
    {
        var grid = FindStockDetailGrid();
        if (grid is null || _vm is null)
        {
            return;
        }

        if (IsSelectionAlreadySynced(grid, _vm.SelectedStockRowsSnapshot))
        {
            return;
        }

        _isSyncingSelectionFromVm = true;
        try
        {
            grid.SelectedItems.Clear();
            foreach (var row in _vm.SelectedStockRowsSnapshot)
            {
                grid.SelectedItems.Add(row);
            }
        }
        finally
        {
            _isSyncingSelectionFromVm = false;
        }
    }

    private static bool IsSelectionAlreadySynced(DataGrid grid, IReadOnlyList<StockRowItem> snapshot)
    {
        if (grid.SelectedItems.Count != snapshot.Count)
        {
            return false;
        }

        for (var i = 0; i < snapshot.Count; i++)
        {
            if (!ReferenceEquals(grid.SelectedItems[i], snapshot[i]))
            {
                return false;
            }
        }

        return true;
    }

}
