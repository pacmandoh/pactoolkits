using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Ui.Interaction;
using PacToolkits.Desktop.Avalonia.ViewModels.Support.Catalog;
using BarcodeGenViewModel = PacToolkits.Desktop.Avalonia.ViewModels.Pages.BarcodeGen;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class BarcodeGen : UserControl
{
    private BarcodeGenViewModel? _vm;

    public BarcodeGen()
    {
        InitializeComponent();
        AttachDrugFilter();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _vm?.PoolRows.CollectionChanged -= OnPoolRowsChanged;

        _vm = DataContext as BarcodeGenViewModel;
        _vm?.PoolRows.CollectionChanged += OnPoolRowsChanged;
    }

    private void OnPoolRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset))
        {
            return;
        }

        Dispatcher.UIThread.Post(ScrollPoolQueueToEnd, DispatcherPriority.Background);
    }

    private void ScrollPoolQueueToEnd()
    {
        if (PoolQueueCard is null)
        {
            return;
        }

        var scroll = PoolQueueCard.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null)
        {
            return;
        }

        var extent = scroll.Extent;
        var viewport = scroll.Viewport;
        var maxOffset = Math.Max(0, extent.Height - viewport.Height);
        scroll.Offset = new Vector(0, maxOffset);
    }

    private void DrugBox_OnKeyDown(object? sender, KeyEventArgs e)
        => _ = AutoCompleteCommit.CommitOnEnter(this, sender, e, "SpecBox", ApplyDrugFilterFromBox);

    private void AttachDrugFilter()
    {
        if (this.FindControl<AutoCompleteBox>("DrugBox") is not { } box)
        {
            return;
        }

        AutoCompleteFilter.AttachDrugOptionFilter(box);
        AutoCompleteCommit.AttachCandidateCommitApply(box, this, "SpecBox", ApplyDrugFilterFromBox);
    }

    private void ApplyDrugFilterFromBox()
    {
        if (DataContext is not BarcodeGenViewModel vm || vm.ApplyDrugFilterCommand.CanExecute(null) != true)
        {
            return;
        }

        vm.ApplyDrugFilterCommand.Execute(null);
    }
}
