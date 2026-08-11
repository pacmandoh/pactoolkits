using System;
using System.ComponentModel;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Ui.Interaction;
using PacToolkits.Desktop.Avalonia.Ui.Layout;
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

        // 集合替换后容器布局尚未稳定，应在下一轮布局完成后再滚动
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
}
