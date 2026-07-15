using System.Collections.Generic;
using Avalonia.Interactivity;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class ScanCodeView : UserControl
{
    private static readonly string[] BrowsingGridNames = { "AutoTaskGrid", "RecentRunGrid", "RetryQueueGrid" };

    public ScanCodeView()
    {
        InitializeComponent();
        AttachDrugFilter();
    }

    private bool _syncingSelection;

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
        if (DataContext is not ScanCode vm)
        {
            return;
        }

        var cmd = vm.ApplyDrugFilterCommand;
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
        }
    }

    private void CodeEditor_OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScanCode vm)
        {
            return;
        }

        var cmd = vm.RequireDrugSpecCommand;
        if (cmd?.CanExecute(null) == true)
        {
            cmd.Execute(null);
        }
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
