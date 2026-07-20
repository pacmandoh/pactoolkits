using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Common;
using DrugIndexViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.DrugIndex;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DrugIndex : UserControl
{
    private readonly PageGridMountScheduler _gridMount;
    private DrugIndexViewModel? _vm;

    public DrugIndex()
    {
        _gridMount = new PageGridMountScheduler(this);
        InitializeComponent();
        DrugGridSlot.GridMounted += OnDrugGridMounted;
        AddHandler(KeyDownEvent, OnEditorAreaKeyDown, RoutingStrategies.Tunnel);
        _gridMount.StartAfterFirstLayout();
        DataContextChanged += OnDrugIndexDataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gridMount.Cancel();
        DetachVm(_vm);

        _vm = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDrugGridMounted(object? sender, DataGrid grid)
        => _vm?.IsDrugGridMounted = true;

    private void OnDrugIndexDataContextChanged(object? sender, EventArgs e)
    {
        DetachVm(_vm);

        _vm = DataContext as DrugIndexViewModel;
        AttachVm(_vm);
        if (_vm is not null && DrugGridSlot.IsMounted)
        {
            _vm.IsDrugGridMounted = true;
        }

        QueueDrugGridMount();
    }

    private void AttachVm(DrugIndexViewModel? vm)
    {
        if (vm is null)
        {
            return;
        }

        vm.PropertyChanged += OnVmPropertyChanged;
        vm.RevealSelected += OnRevealSelected;
    }

    private void DetachVm(DrugIndexViewModel? vm)
    {
        if (vm is null)
        {
            return;
        }

        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.RevealSelected -= OnRevealSelected;
    }

    private void OnRevealSelected()
    {
        var item = _vm?.Selected;
        var grid = DataGridInteractionHelper.FindDeferredGrid(this, "DrugGrid");
        if (item is null || grid is null)
        {
            return;
        }

        // ReplaceAll may still be settling slots; scroll after the next layout pass.
        Dispatcher.UIThread.Post(
            () => DataGridInteractionHelper.TryScrollIntoView(grid, item),
            DispatcherPriority.Loaded);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DrugIndexViewModel.IsItemsEmpty)
            or nameof(DrugIndexViewModel.IsSectionPending))
        {
            QueueDrugGridMount();
        }
    }

    private void QueueDrugGridMount()
    {
        if (!DrugGridSlot.IsMounted)
        {
            _gridMount.RequestMount(DrugGridSlot, 0);
        }
    }

    private void OnEditorAreaKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab && e.Key != Key.Enter)
        {
            return;
        }

        if (this.FindControl<Control>("EditorCard") is not { } editorCard)
        {
            return;
        }

        var inputs = EnumerateEditorInputs(editorCard).ToList();
        InputFocusHelper.TryHandleTabCycle(this, e, inputs);
    }

    private static IEnumerable<Control> EnumerateEditorInputs(Control root) =>
        InputFocusHelper.EnumerateInputs(root, typeof(TextBox), typeof(NumericUpDown));
}
