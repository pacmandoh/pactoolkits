using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Common;
using DashboardViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.Dashboard;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class DashboardTxn : UserControl
{
    private readonly PageGridMountScheduler _gridMount;
    private DashboardViewModel? _vm;

    public DashboardTxn()
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
        QueueGrids();
    }

    private void SetViewModel(DashboardViewModel? vm)
    {
        _vm?.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = vm;
        _vm?.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.IsRecentTxnsEmpty) or nameof(DashboardViewModel.IsTxnTrendEmpty))
        {
            QueueGrids();
        }
    }

    private void QueueGrids()
    {
        if (_vm is not { } vm)
        {
            return;
        }

        if (!vm.IsRecentTxnsEmpty && !TxnDetailGridSlot.IsMounted)
        {
            _gridMount.RequestMount(TxnDetailGridSlot, 0);
        }

        if (!vm.IsTxnTrendEmpty && !TxnTrendGridSlot.IsMounted)
        {
            _gridMount.RequestMount(TxnTrendGridSlot, 1);
        }
    }
}
