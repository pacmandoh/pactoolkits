using Avalonia.Controls;
using System.Collections.Generic;
using System.Collections;

namespace pactoolkits_ui.Common;

public static class DataGridInteractionHelper
{
    public static void ClearSelection(DataGrid? grid)
    {
        if (grid is null) return;
        try
        {
            grid.SelectedItem = null;
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("DataGridInteraction", "grid.clear_selection_item.fail", "Failed to clear selected item", ex);
        }

        try
        {
            grid.SelectedIndex = -1;
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("DataGridInteraction", "grid.clear_selection_index.fail", "Failed to clear selected index", ex);
        }

        try
        {
            grid.SelectedItems?.Clear();
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("DataGridInteraction", "grid.clear_selection_items.fail", "Failed to clear selected items", ex);
        }
    }

    public static List<object> ReadSelectedItems(DataGrid grid)
    {
        var list = new List<object>();
        try
        {
            foreach (var it in grid.SelectedItems)
            {
                if (it is not null)
                    list.Add(it);
            }
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("DataGridInteraction", "grid.read_selected_items.fail", "Failed to read selected items", ex);
        }

        return list;
    }

    public static DataGrid? FindGridByRowItem(Control host, object? rowItem, params string[] gridNames)
    {
        if (rowItem is null || gridNames.Length == 0)
            return null;

        foreach (var name in gridNames)
        {
            var grid = host.FindControl<DataGrid>(name);
            if (ContainsItemReference(grid?.ItemsSource, rowItem))
                return grid;
        }

        return null;
    }

    private static bool ContainsItemReference(IEnumerable? source, object rowItem)
    {
        if (source is null)
            return false;

        foreach (var item in source)
        {
            if (ReferenceEquals(item, rowItem))
                return true;
        }

        return false;
    }
}
