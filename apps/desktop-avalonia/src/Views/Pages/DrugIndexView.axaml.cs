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
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DrugIndexView : UserControl
{
    private readonly PageGridMountScheduler _gridMount;
    private DrugIndexViewModel? _vm;

    public DrugIndexView()
    {
        _gridMount = new PageGridMountScheduler(this);
        InitializeComponent();
        AddHandler(KeyDownEvent, OnEditorAreaKeyDown, RoutingStrategies.Tunnel);
        _gridMount.StartAfterFirstLayout();
        DataContextChanged += OnDrugIndexDataContextChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gridMount.Cancel();
        _vm?.PropertyChanged -= OnVmPropertyChanged;

        _vm = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDrugIndexDataContextChanged(object? sender, EventArgs e)
    {
        _vm?.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as DrugIndexViewModel;
        _vm?.PropertyChanged += OnVmPropertyChanged;
        QueueDrugGridMount();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DrugIndexViewModel.IsItemsEmpty))
        {
            QueueDrugGridMount();
        }
    }

    private void QueueDrugGridMount()
    {
        if (_vm?.IsItemsEmpty == false && !DrugGridSlot.IsMounted)
        {
            _gridMount.RequestMount(DrugGridSlot, 0);
        }
    }

    private void DrugIndexSearchBox_OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        InputFocusHelper.FocusControlByName(this, "DrugIndexSearchButton", DispatcherPriority.Background);
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
