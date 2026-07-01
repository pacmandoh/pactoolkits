using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.LogicalTree;
using global::Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class InventoryOverviewView : UserControl
{
    private readonly PageGridMountScheduler _gridMount;
    private InventoryOverview? _vm;
    private Control? _reassignRoot;
    private AutoCompleteBox? _reassignDrugBox;

    public InventoryOverviewView()
    {
        _gridMount = new PageGridMountScheduler(this);
        InitializeComponent();
        ReassignPanelHost.ContentLoaded += OnPanelHostContentLoaded;
        WireStockDetailGridSlot();
        _gridMount.StartAfterFirstLayout();
        DataContextChanged += OnDataContextChanged;

        LowStockGridSlot.GridMounted += (_, grid) =>
        {
            _vm?.MarkLowGridMounted();
            grid.PointerReleased += OnLowOrMissingGridPointerReleased;
        };
        MissingGridSlot.GridMounted += (_, grid) =>
        {
            _vm?.MarkMissingGridMounted();
            grid.PointerReleased += OnLowOrMissingGridPointerReleased;
        };
        AggGridSlot.GridMounted += (_, _) => _vm?.MarkAggGridMounted();
    }

    private void OnPanelHostContentLoaded(object? sender, Control root)
    {
        _reassignRoot = root;
        AttachDrugFilter();
    }

    private void AttachDrugFilter()
    {
        if (_reassignDrugBox is not null)
        {
            return;
        }

        var root = _reassignRoot ?? ReassignPanelHost.Content as Control;
        // Deferred template x:Name is not on FindControl scope; logical tree has the drug box.
        if (root?.GetLogicalDescendants().OfType<AutoCompleteBox>().FirstOrDefault() is not { } box)
        {
            return;
        }

        _reassignDrugBox = box;
        AutoCompleteFilter.AttachDrugOptionFilter(box);
        AutoCompleteCommit.AttachCandidateCommitApply(
            box,
            ReassignRoot,
            "ReassignSpecBox",
            ApplyDrugFilterFromBox);
    }

    private Control ReassignRoot => _reassignRoot ?? ReassignPanelHost.Content as Control ?? this;

    private void QueueAggGridMount()
    {
        if (_vm?.IsAggMode == true && !AggGridSlot.IsMounted)
        {
            _gridMount.RequestMount(AggGridSlot, 2);
        }
    }

    private void QueueLowGridMount()
    {
        if (_vm?.IsLowMode == true && !LowStockGridSlot.IsMounted)
        {
            _gridMount.RequestMount(LowStockGridSlot, 3);
        }
    }

    private void QueueMissingGridMount()
    {
        if (_vm?.IsMissingMode == true && !MissingGridSlot.IsMounted)
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
            _vm?.MarkDetailGridMounted();
            grid.BeginningEdit += OnStockBeginningEdit;
            grid.CellEditEnding += OnStockCellEditEnding;
            grid.CellPointerPressed += OnStockCellPointerPressed;
            DataGridRowSelection.AddSelectionChangedHandler(grid, OnStockRowSelectionChanged);
            SyncStockEditClass();
        };
    }

    private void QueueStockDetailGridMount()
    {
        if (_vm?.IsDetailMode == true && !StockDetailGridSlot.IsMounted)
        {
            _gridMount.RequestMount(StockDetailGridSlot, 0);
        }
    }

    private async void OnLowOrMissingGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            if (DataContext is not InventoryOverview vm)
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
            if (DataContext is not InventoryOverview vm)
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
            if (DataContext is not InventoryOverview vm)
            {
                return;
            }

            var row = e.Row?.DataContext as StockRowItem;

            if (sender is DataGrid grid && row is not null)
            {
                if (DataGridInteractionHelper.IsLeftClick(e.PointerPressedEventArgs, grid))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
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
            if (DataContext is not InventoryOverview vm)
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

    private void OnStockRowSelectionChanged(object? sender, DataGridRowSelectionChangedEventArgs e)
    {
        if (DataContext is not InventoryOverview vm)
        {
            return;
        }

        vm.SyncReassignSelectionFromRows();
    }

    private void ReassignDrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteCommit.CommitOnEnter(
            ReassignRoot,
            sender,
            e,
            "ReassignSpecBox",
            ApplyDrugFilterFromBox);
    }

    private void ApplyDrugFilterFromBox()
    {
        if (DataContext is not InventoryOverview vm)
        {
            return;
        }

        if (vm.ApplyDrugFilterCommand.CanExecute(null))
        {
            vm.ApplyDrugFilterCommand.Execute(null);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _vm?.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as InventoryOverview;
        _vm?.PropertyChanged += OnVmPropertyChanged;

        if (_vm is not null)
        {
            SyncMountedGridFlags(_vm);
        }

        SyncStockEditClass();
        QueueStockDetailGridMount();
        TryQueueActiveModeGrid();
    }

    private void SyncMountedGridFlags(InventoryOverview vm)
    {
        if (StockDetailGridSlot.IsMounted)
        {
            vm.MarkDetailGridMounted();
        }

        if (AggGridSlot.IsMounted)
        {
            vm.MarkAggGridMounted();
        }

        if (LowStockGridSlot.IsMounted)
        {
            vm.MarkLowGridMounted();
        }

        if (MissingGridSlot.IsMounted)
        {
            vm.MarkMissingGridMounted();
        }
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
        _reassignRoot = null;

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

        if (e.PropertyName == nameof(InventoryOverview.IsStockEditEnabled))
        {
            SyncStockEditClass();
        }

        if (e.PropertyName == nameof(InventoryOverview.ModeIndex))
        {
            QueueStockDetailGridMount();
            TryQueueActiveModeGrid();
        }

        if (e.PropertyName == nameof(InventoryOverview.IsStockEmpty))
        {
            QueueStockDetailGridMount();
        }

        if (e.PropertyName is nameof(InventoryOverview.IsAggEmpty)
            or nameof(InventoryOverview.IsLowEmpty)
            or nameof(InventoryOverview.IsMissingEmpty))
        {
            TryQueueActiveModeGrid();
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

}
