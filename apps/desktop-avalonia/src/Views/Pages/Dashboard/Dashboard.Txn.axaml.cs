using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Ui.Layout;
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
        TxnDetailGridSlot.GridMounted += (_, _) => _vm?.IsTxnDetailGridMounted = true;
        TxnTrendGridSlot.GridMounted += (_, _) => _vm?.IsTxnTrendGridMounted = true;
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
        if (_vm is null)
        {
            return;
        }

        if (TxnDetailGridSlot.IsMounted)
        {
            _vm.IsTxnDetailGridMounted = true;
        }

        if (TxnTrendGridSlot.IsMounted)
        {
            _vm.IsTxnTrendGridMounted = true;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.IsRecentTxnsEmpty)
            or nameof(DashboardViewModel.IsTxnTrendEmpty)
            or nameof(DashboardViewModel.IsTxnPanelDetailMode)
            or nameof(DashboardViewModel.IsTxnPanelTrendMode))
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

        if (vm.RecentTxns.Count > 0 && !TxnDetailGridSlot.IsMounted)
        {
            _gridMount.RequestMount(TxnDetailGridSlot, 0);
        }

        if (vm.TxnTrendRows.Count > 0 && !TxnTrendGridSlot.IsMounted)
        {
            _gridMount.RequestMount(TxnTrendGridSlot, 1);
        }
    }
}
