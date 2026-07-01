using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DrugIndexView : UserControl
{
    private readonly PageGridMountScheduler _gridMount;
    private DrugIndex? _vm;

    public DrugIndexView()
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

        _vm = DataContext as DrugIndex;
        AttachVm(_vm);
        if (_vm is not null && DrugGridSlot.IsMounted)
        {
            _vm.IsDrugGridMounted = true;
        }

        QueueDrugGridMount();
    }

    private void AttachVm(DrugIndex? vm)
    {
        if (vm is null)
        {
            return;
        }

        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void DetachVm(DrugIndex? vm)
    {
        if (vm is null)
        {
            return;
        }

        vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DrugIndex.IsItemsEmpty)
            or nameof(DrugIndex.IsSectionPending))
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
