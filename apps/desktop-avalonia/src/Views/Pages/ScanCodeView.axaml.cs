using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class ScanCodeView : UserControl
{
    private static readonly string[] BrowsingGridNames = { "AutoTaskGrid", "RecentRunGrid", "RetryQueueGrid" };

    private readonly PageGridMountScheduler _gridMount;
    private ScanCodeViewModel? _vm;
    private bool _autoFetchGridsWired;

    public ScanCodeView()
    {
        _gridMount = new PageGridMountScheduler(this);
        InitializeComponent();
        AttachDrugFilter();
        AutoFetchTabHost.ContentLoaded += OnAutoFetchTabContentLoaded;
        _gridMount.StartAfterFirstLayout();
        DataContextChanged += OnScanCodeDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnScanCodeDataContextChanged(object? sender, EventArgs e)
    {
        _vm?.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as ScanCodeViewModel;
        _vm?.PropertyChanged += OnVmPropertyChanged;
        TryQueueAutoFetchGrids();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ScanCodeViewModel.IsAutoFetchTab):
                TryQueueAutoFetchGrids();
                break;
            case nameof(ScanCodeViewModel.IsAutoTasksEmpty):
                TryQueueAutoFetchGrid("AutoTaskGridSlot", 0);
                break;
            case nameof(ScanCodeViewModel.IsRecentRunsEmpty):
                TryQueueAutoFetchGrid("RecentRunGridSlot", 1);
                break;
            case nameof(ScanCodeViewModel.IsRetryQueueEmpty):
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

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => AttachTraceCodeScrollSync();

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _gridMount.Cancel();
        _vm?.PropertyChanged -= OnVmPropertyChanged;

        _vm = null;
        _autoFetchGridsWired = false;
        DetachTraceCodeScrollSync();
    }

    private bool _syncingSelection;
    private bool _syncingScroll;
    private ScrollViewer? _traceCodeInputScroll;
    private ScrollViewer? _traceCodePreviewScroll;

    private void AttachTraceCodeScrollSync()
    {
        DetachTraceCodeScrollSync();

        if (this.FindControl<TextBox>("TraceCodeInput") is not { } input)
        {
            return;
        }

        if (this.FindControl<ScrollViewer>("TraceCodePreviewScroll") is not { } preview)
        {
            return;
        }

        _traceCodePreviewScroll = preview;
        preview.ScrollChanged += OnTraceCodePreviewScrollChanged;

        void AttachInputScroll()
        {
            if (_traceCodeInputScroll is not null)
            {
                return;
            }

            var inputScroll = input.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            if (inputScroll is null)
            {
                return;
            }

            _traceCodeInputScroll = inputScroll;
            inputScroll.ScrollChanged += OnTraceCodeInputScrollChanged;
        }

        input.TemplateApplied += (_, _) => AttachInputScroll();
        AttachInputScroll();
    }

    private void DetachTraceCodeScrollSync()
    {
        if (_traceCodeInputScroll is { } inputScroll)
        {
            inputScroll.ScrollChanged -= OnTraceCodeInputScrollChanged;
            _traceCodeInputScroll = null;
        }

        if (_traceCodePreviewScroll is { } previewScroll)
        {
            previewScroll.ScrollChanged -= OnTraceCodePreviewScrollChanged;
            _traceCodePreviewScroll = null;
        }
    }

    private void OnTraceCodeInputScrollChanged(object? sender, ScrollChangedEventArgs e)
        => SyncTraceCodeScroll(fromInput: true);

    private void OnTraceCodePreviewScrollChanged(object? sender, ScrollChangedEventArgs e)
        => SyncTraceCodeScroll(fromInput: false);

    private void SyncTraceCodeScroll(bool fromInput)
    {
        if (_syncingScroll || _traceCodeInputScroll is null || _traceCodePreviewScroll is null)
        {
            return;
        }

        var source = fromInput ? _traceCodeInputScroll : _traceCodePreviewScroll;
        var target = fromInput ? _traceCodePreviewScroll : _traceCodeInputScroll;

        _syncingScroll = true;
        try
        {
            target.Offset = new Vector(source.Offset.X, source.Offset.Y);
        }
        finally
        {
            _syncingScroll = false;
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

    private void ApplyDrugFilterFromBox()
    {
        if (DataContext is not ScanCodeViewModel vm)
        {
            return;
        }

        var cmd = vm.ApplyDrugFilterCommand;
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
        }
    }

    private void CodeEditor_OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (DataContext is not ScanCodeViewModel vm)
        {
            return;
        }

        var cmd = vm.EnsureEditorContextCommand;
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
        }
    }

    private void TraceCodeInputBlocked_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ScanCodeViewModel vm)
        {
            return;
        }

        vm.EnsureEditorContextCommand.Execute(null);
        e.Handled = true;
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
