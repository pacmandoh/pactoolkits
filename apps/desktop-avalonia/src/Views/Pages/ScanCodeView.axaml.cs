using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class ScanCodeView : UserControl
{
    private static readonly string[] BrowsingGridNames = { "AutoTaskGrid", "RecentRunGrid", "RetryQueueGrid" };
    private bool _syncingSelection;

    public ScanCodeView()
    {
        InitializeComponent();
        AttachDrugFilter();
    }

    private void DrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        _ = AutoCompleteHelper.HandleEnterCommitAndApply(
            this,
            sender,
            e,
            "SpecBox",
            () =>
            {
                if (DataContext is not PacToolkits.Desktop.Avalonia.ViewModels.Pages.ScanCodeViewModel vm)
                {
                    return;
                }

                var cmd = vm.ApplyDrugFilterCommand;
                if (cmd?.CanExecute(null) == true)
                {
                    cmd.Execute(null);
                }
            });
    }

    private void CodeEditor_OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not PacToolkits.Desktop.Avalonia.ViewModels.Pages.ScanCodeViewModel vm)
        {
            return;
        }

        var cmd = vm.EnsureEditorContextCommand;
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
            if (this.FindControl<DataGrid>(name) is { } grid)
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

        AutoCompleteHelper.AttachDrugOptionFilter(box);
    }

}
