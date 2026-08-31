using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Ui.Layout;
using DashboardViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.Dashboard;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DashboardAbnormal : UserControl
{
    private readonly PageGridMountScheduler _gridMount;
    private DashboardViewModel? _vm;

    public DashboardAbnormal()
    {
        _gridMount = new PageGridMountScheduler(this);
        InitializeComponent();
        AbnormalGridSlot.GridMounted += (_, _) => _vm?.IsAbnormalGridMounted = true;
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
        if (_vm is not null && AbnormalGridSlot.IsMounted)
        {
            _vm.IsAbnormalGridMounted = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DashboardViewModel.IsAbnormalEmpty))
        {
            QueueGrid();
        }
    }

    private void QueueGrid()
    {
        if (_vm is { AbnormalQueue.Count: > 0 } && !AbnormalGridSlot.IsMounted)
        {
            _gridMount.RequestMount(AbnormalGridSlot, 0);
        }
    }
}
