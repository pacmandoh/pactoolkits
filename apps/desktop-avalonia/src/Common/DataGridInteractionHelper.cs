using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Behaviors;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class DataGridInteractionHelper
{
    public static void ClearSelection(DataGrid? grid)
    {
        if (grid is null)
        {
            return;
        }

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

        ClearCurrentCell(grid);
    }

    public static void ClearCurrentCell(DataGrid? grid)
    {
        if (grid is null)
        {
            return;
        }

        try
        {
            grid.CurrentColumn = null;
        }
        catch (System.Exception ex)
        {
            AppLog.Warn("DataGridInteraction", "grid.clear_current_column.fail", "Failed to clear current column", ex);
        }
    }

    public static List<object> ReadSelectedItems(DataGrid grid)
    {
        var capacity = 0;
        try
        {
            capacity = grid.SelectedItems?.Count ?? 0;
        }
        catch
        {
            capacity = 0;
        }

        var list = capacity > 0 ? new List<object>(capacity) : new List<object>();
        try
        {
            var selectedItems = grid.SelectedItems;
            if (selectedItems is null)
            {
                return list;
            }

            foreach (var it in selectedItems)
            {
                if (it is not null)
                {
                    list.Add(it);
                }
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
        {
            return null;
        }

        foreach (var name in gridNames)
        {
            var grid = host.FindControl<DataGrid>(name);
            if (ContainsItemReference(grid?.ItemsSource, rowItem))
            {
                return grid;
            }
        }

        return null;
    }

    public static DataGridRow? FindRowFromPointerSource(object? source, out bool hitRowHeader)
    {
        hitRowHeader = false;
        var current = source as StyledElement;
        while (current is not null)
        {
            if (current is DataGridRowHeader)
            {
                hitRowHeader = true;
            }

            if (current is DataGridCell cell && IsIndexColumnCell(cell))
            {
                hitRowHeader = true;
            }

            if (current is DataGridRow row)
            {
                return row;
            }

            current = current.Parent as StyledElement;
        }

        return null;
    }

    public static bool IsIndexColumnCell(DataGridCell cell)
    {
        var grid = cell.FindAncestorOfType<DataGrid>();
        if (grid is null || !DataGridIndexColumnBehavior.GetEnabled(grid) || !DataGridIndexColumnBehavior.GetIsVisible(grid))
        {
            return false;
        }

        if (grid.Columns.Count == 0 || !DataGridIndexColumnBehavior.IsIndexColumn(grid.Columns[0]))
        {
            return false;
        }

        var row = cell.FindAncestorOfType<DataGridRow>();
        if (row is null)
        {
            return false;
        }

        foreach (var child in row.GetVisualDescendants())
        {
            if (child is DataGridCell firstCell)
            {
                return ReferenceEquals(firstCell, cell);
            }
        }

        return false;
    }

    public static bool TrySelectRowFromPointer(
        DataGrid grid,
        object? pointerSource,
        bool requireRowHeader,
        out object? rowData,
        out bool hitRowHeader)
    {
        rowData = null;
        var row = FindRowFromPointerSource(pointerSource, out hitRowHeader);
        if (row?.DataContext is null)
        {
            return false;
        }

        if (requireRowHeader && !hitRowHeader)
        {
            return false;
        }

        rowData = row.DataContext;
        grid.SelectedItem = rowData;
        return true;
    }

    public static bool IsLeftClick(PointerPressedEventArgs e, Control relativeTo)
    {
        var p = e.GetCurrentPoint(relativeTo).Properties;
        return p.IsLeftButtonPressed && !p.IsRightButtonPressed;
    }

    public static bool IsRightClick(PointerPressedEventArgs e, Control relativeTo)
        => e.GetCurrentPoint(relativeTo).Properties.IsRightButtonPressed;

    private static bool ContainsItemReference(IEnumerable? source, object rowItem)
    {
        if (source is null)
        {
            return false;
        }

        foreach (var item in source)
        {
            if (ReferenceEquals(item, rowItem))
            {
                return true;
            }
        }

        return false;
    }

    public static DataGrid? FindDeferredGrid(Control root, string name)
    {
        if (root.FindControl<DataGrid>(name) is { } direct)
        {
            return direct;
        }

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is DataGrid grid &&
                string.Equals(grid.Name, name, StringComparison.Ordinal))
            {
                return grid;
            }
        }

        return null;
    }
}
