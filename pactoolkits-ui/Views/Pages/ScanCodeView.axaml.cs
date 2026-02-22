using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia;
using System.Collections.Generic;
using pactoolkits_ui.Common;

namespace pactoolkits_ui.Views.Pages;

public partial class ScanCodeView : UserControl
{
    private static readonly string[] BrowsingGridNames = { "AutoTaskGrid", "RecentRunGrid", "RetryQueueGrid" };
    private bool _syncingSelection;

    public ScanCodeView()
    {
        InitializeComponent();
    }

    private void DrugBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not AutoCompleteBox box) return;
        if (e.Key != Key.Enter) return;

        e.Handled = true;
        TriggerApplyDrugFilter(box);
    }

    private void TriggerApplyDrugFilter(AutoCompleteBox box)
    {
        Dispatcher.UIThread.Post(() =>
        {
            InputFocusHelper.CommitAutoCompleteInput(box);

            if (DataContext is pactoolkits_ui.ViewModels.Pages.ScanCodeViewModel vm)
            {
                var cmd = vm.ApplyDrugFilterCommand;
                if (cmd?.CanExecute(null) == true)
                    cmd.Execute(null);
            }

            InputFocusHelper.FocusControlByName(this, "SpecBox");
        }, DispatcherPriority.Input);
    }

    private void CodeEditor_OnGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not pactoolkits_ui.ViewModels.Pages.ScanCodeViewModel vm)
            return;

        var cmd = vm.EnsureEditorContextCommand;
        if (cmd?.CanExecute(null) == true)
            cmd.Execute(null);
    }

    private void OnBrowsingGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        if (sender is not DataGrid activeGrid) return;

        _syncingSelection = true;
        try
        {
            foreach (var grid in GetBrowsingGrids())
            {
                if (!ReferenceEquals(grid, activeGrid))
                    grid.SelectedItem = null;
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
                yield return grid;
        }
    }

}
