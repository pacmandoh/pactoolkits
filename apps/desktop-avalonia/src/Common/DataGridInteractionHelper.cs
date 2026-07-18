using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
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

        if (grid.SelectionMode == DataGridSelectionMode.Extended)
        {
            try
            {
                grid.SelectedItems?.Clear();
            }
            catch (System.Exception ex)
            {
                AppLog.Warn("DataGridInteraction", "grid.clear_selection_items.fail", "Failed to clear selected items", ex);
            }
        }
    }

    internal static void ClearOnPageChange(DataGrid? grid, int oldIndex, int newIndex)
    {
        if (grid is null || oldIndex == newIndex)
        {
            return;
        }

        ClearNativeRowHighlight(grid);
        Dispatcher.UIThread.Post(() => ClearNativeRowHighlight(grid), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Clears DataGrid row highlight/focus only. Does not touch <see cref="ISelectableRow.IsSelected"/>.
    /// </summary>
    public static void ClearNativeRowHighlight(DataGrid? grid)
    {
        if (grid is null)
        {
            return;
        }

        ClearSelection(grid);

        if (!grid.IsKeyboardFocusWithin)
        {
            return;
        }

        if (TopLevel.GetTopLevel(grid) is InputElement top)
        {
            top.Focus();
        }
    }

    public static void TrySetCurrentColumn(DataGrid grid, DataGridColumn? column)
    {
        if (column is null)
        {
            return;
        }

        try
        {
            grid.CurrentColumn = column;
        }
        catch (Exception ex)
        {
            AppLog.Warn("DataGridInteraction", "grid.set_current_column.fail", "Failed to set current column", ex);
        }
    }

    private static DataGridRow? FindRowFromPointerSource(object? source, out bool hitRowHeader)
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

    private static bool IsIndexColumnCell(DataGridCell cell)
    {
        var grid = cell.FindAncestorOfType<DataGrid>();
        if (grid is null || !DataGridIndexColumn.GetEnabled(grid) || !DataGridIndexColumn.GetIsVisible(grid))
        {
            return false;
        }

        if (grid.Columns.Count == 0 || !DataGridIndexColumn.IsIndexColumn(grid.Columns[0]))
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

    public static class Rules
    {
        public static string? SortPath(BindingBase? binding)
        {
            if (binding is null)
            {
                return null;
            }

            return binding switch
            {
                Binding reflection => reflection.Path,
                _ => binding.GetType().GetProperty("Path")?.GetValue(binding)?.ToString()
            };
        }

        public static int SelectionInsertIndex(bool indexColumnFirst)
            => indexColumnFirst ? 1 : 0;

        public static DataGridIndexHeaderFace IndexHeaderFace(bool filterActive, bool hasActiveSort)
        {
            if (filterActive)
            {
                return DataGridIndexHeaderFace.ClearFilter;
            }

            if (hasActiveSort)
            {
                return DataGridIndexHeaderFace.ClearSort;
            }

            return DataGridIndexHeaderFace.Default;
        }

        public static bool? SelectAllTriState(int selectedCount, int totalCount)
        {
            if (totalCount == 0)
            {
                return false;
            }

            if (selectedCount == 0)
            {
                return false;
            }

            if (selectedCount == totalCount)
            {
                return true;
            }

            return null;
        }
    }
}
