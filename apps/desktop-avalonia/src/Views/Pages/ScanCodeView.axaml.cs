using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Interactivity;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class ScanCodeView : UserControl
{
    private static readonly string[] BrowsingGridNames = { "AutoTaskGrid", "RecentRunGrid", "RetryQueueGrid" };

    private readonly PageGridMountScheduler _gridMount;
    private ScanCode? _vm;
    private bool _autoFetchGridsWired;

    public ScanCodeView()
    {
        _gridMount = new PageGridMountScheduler(this);
        InitializeComponent();
        AttachDrugFilter();
        AutoFetchTabHost.ContentLoaded += OnAutoFetchTabContentLoaded;
        _gridMount.StartAfterFirstLayout();
        DataContextChanged += OnScanCodeDataContextChanged;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnScanCodeDataContextChanged(object? sender, EventArgs e)
    {
        _vm?.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as ScanCode;
        _vm?.PropertyChanged += OnVmPropertyChanged;
        TryQueueAutoFetchGrids();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ScanCode.IsAutoFetchTab):
                TryQueueAutoFetchGrids();
                break;
            case nameof(ScanCode.IsAutoTasksEmpty):
                TryQueueAutoFetchGrid("AutoTaskGridSlot", 0);
                break;
            case nameof(ScanCode.IsRecentRunsEmpty):
                TryQueueAutoFetchGrid("RecentRunGridSlot", 1);
                break;
            case nameof(ScanCode.IsRetryQueueEmpty):
                TryQueueAutoFetchGrid("RetryQueueGridSlot", 2);
                break;
        }
    }

    private void OnAutoFetchTabContentLoaded(object? sender, Control root)
    {
        WireAutoFetchGridSlots(root);
        TryQueueAutoFetchGrids();
    }

    private void WireAutoFetchGridSlots(Control root)
    {
        if (_autoFetchGridsWired)
        {
            return;
        }

        _autoFetchGridsWired = true;
        WireBrowsingSlot(root, "AutoTaskGridSlot");
        WireBrowsingSlot(root, "RecentRunGridSlot");
        WireBrowsingSlot(root, "RetryQueueGridSlot");
    }

    private void WireBrowsingSlot(Control root, string slotName)
    {
        if (root.FindControl<DeferredGridSlot>(slotName) is not { } slot)
        {
            return;
        }

        slot.GridMounted += (_, grid) => grid.SelectionChanged += OnBrowsingGridSelectionChanged;
    }

    private void TryQueueAutoFetchGrids()
    {
        if (_vm?.IsAutoFetchTab != true || AutoFetchTabHost.Content is not Control root)
        {
            return;
        }

        WireAutoFetchGridSlots(root);
        TryQueueAutoFetchGrid("AutoTaskGridSlot", 0);
        TryQueueAutoFetchGrid("RecentRunGridSlot", 1);
        TryQueueAutoFetchGrid("RetryQueueGridSlot", 2);
    }

    private void TryQueueAutoFetchGrid(string slotName, int priority)
    {
        if (_vm?.IsAutoFetchTab != true || AutoFetchTabHost.Content is not Control root)
        {
            return;
        }

        if (root.FindControl<DeferredGridSlot>(slotName) is not { } slot || slot.IsMounted)
        {
            return;
        }

        if (!MountAutoFetchGrid(slotName))
        {
            return;
        }

        _gridMount.RequestMount(slot, priority);
    }

    private bool MountAutoFetchGrid(string slotName)
    {
        if (_vm is null)
        {
            return false;
        }

        return slotName switch
        {
            "AutoTaskGridSlot" => !_vm.IsAutoTasksEmpty,
            "RecentRunGridSlot" => !_vm.IsRecentRunsEmpty,
            "RetryQueueGridSlot" => !_vm.IsRetryQueueEmpty,
            _ => false
        };
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _gridMount.Cancel();
        _vm?.PropertyChanged -= OnVmPropertyChanged;

        _vm = null;
        _autoFetchGridsWired = false;
    }

    private bool _syncingSelection;

    private void DrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteCommit.CommitOnEnter(
            this,
            sender,
            e,
            "SpecBox",
            ApplyDrugFilterFromBox);
    }

    private void ApplyDrugFilterFromBox()
    {
        if (DataContext is not ScanCode vm)
        {
            return;
        }

        var cmd = vm.ApplyDrugFilterCommand;
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
        }
    }

    private void CodeEditor_OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScanCode vm)
        {
            return;
        }

        var cmd = vm.RequireDrugSpecCommand;
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
        }
    }

    private void OnBrowsingGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (sender is not DataGrid activeGrid)
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            foreach (var grid in GetBrowsingGrids())
            {
                if (!ReferenceEquals(grid, activeGrid))
                {
                    grid.SelectedItem = null;
                }
            }
        }
        finally
        {
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

    private void AttachDrugFilter()
    {
        if (this.FindControl<AutoCompleteBox>("DrugBox") is not { } box)
        {
            return;
        }

        AutoCompleteFilter.AttachDrugOptionFilter(box);
        AutoCompleteCommit.AttachCandidateCommitApply(box, this, "SpecBox", ApplyDrugFilterFromBox);
    }
}
