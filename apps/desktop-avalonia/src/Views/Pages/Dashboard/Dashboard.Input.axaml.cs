using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Ui.Layout;
using DashboardViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.Dashboard;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DashboardInput : UserControl
{
    private readonly PageGridMountScheduler _gridMount;
    private DashboardViewModel? _vm;

    public DashboardInput()
    {
        _gridMount = new PageGridMountScheduler(this);
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _gridMount.StartAfterFirstLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _gridMount.Cancel();
        SetViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        SetViewModel(DataContext as DashboardViewModel);
        QueueGrid();
    }

    private void SetViewModel(DashboardViewModel? vm)
    {
        _vm?.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = vm;
        _vm?.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.IsEntryRecentEmpty))
        {
            QueueGrid();
        }
    }

    private void QueueGrid()
    {
        if (_vm is { EntryRecent.Count: > 0 } && !EntryGridSlot.IsMounted)
        {
            _gridMount.RequestMount(EntryGridSlot, 0);
        }
    }
}
