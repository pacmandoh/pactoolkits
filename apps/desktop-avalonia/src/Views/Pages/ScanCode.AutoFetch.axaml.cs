using System.Collections.Generic;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Views.Pages;

public partial class ScanCodeAutoFetch : UserControl
{
    private static readonly string[] BrowsingGridNames = { "AutoTaskGrid", "RecentRunGrid", "RetryQueueGrid" };
    private bool _syncingSelection;

    public ScanCodeAutoFetch()
    {
        InitializeComponent();
    }

    private void OnBrowsingGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || sender is not DataGrid activeGrid)
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
}
