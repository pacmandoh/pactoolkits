using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using pactoolkits_ui.Services.Infrastructure;

namespace pactoolkits_ui.Common;

public static class GridContextMenuActions
{
    public static DataGrid? ResolveGrid(Control host, MenuItem? menuItem, object? rowItem, params string[] gridNames)
        => GridContextMenuHelper.FindOwnerGrid(menuItem)
           ?? DataGridInteractionHelper.FindGridByRowItem(host, rowItem, gridNames);

    public static async Task CopyAsync(
        IClipboardService clipboard,
        Control host,
        MenuItem? menuItem,
        object? rowItem,
        string[] gridNames,
        string[] preferredProps,
        Func<DataGrid, List<object>, List<object>>? selectedRowsTransform = null)
    {
        var grid = ResolveGrid(host, menuItem, rowItem, gridNames);
        if (grid is null)
        {
            await GridContextMenuHelper.CopyRowAsTextAsync(clipboard, menuItem, rowItem, preferredProps);
            return;
        }

        if (selectedRowsTransform is null)
        {
            var selectedCount = grid.SelectedItems?.Count ?? 0;
            if (selectedCount > 1)
            {
                var selected = DataGridInteractionHelper.ReadSelectedItems(grid);
                await GridContextMenuHelper.CopyRowsAsTextAsync(clipboard, selected, preferredProps);
                return;
            }

            await GridContextMenuHelper.CopyRowAsTextAsync(clipboard, grid, rowItem, preferredProps);
            return;
        }

        var transformed = selectedRowsTransform(grid, DataGridInteractionHelper.ReadSelectedItems(grid));
        if (transformed.Count > 1)
        {
            await GridContextMenuHelper.CopyRowsAsTextAsync(clipboard, transformed, preferredProps);
            return;
        }

        await GridContextMenuHelper.CopyRowAsTextAsync(clipboard, grid, rowItem, preferredProps);
    }

    public static async Task CopySafeAsync(
        IClipboardService clipboard,
        Control host,
        MenuItem? menuItem,
        object? rowItem,
        string[] gridNames,
        string[] preferredProps,
        string logScope,
        string logEvent,
        string logMessage,
        Func<DataGrid, List<object>, List<object>>? selectedRowsTransform = null)
    {
        try
        {
            await CopyAsync(
                clipboard,
                host,
                menuItem,
                rowItem,
                gridNames,
                preferredProps,
                selectedRowsTransform).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Warn(logScope, logEvent, logMessage, ex);
        }
    }

    public static void SelectAll(
        Control host,
        MenuItem? menuItem,
        object? rowItem,
        string[] gridNames,
        Action<DataGrid>? onSelected = null)
    {
        var grid = ResolveGrid(host, menuItem, rowItem, gridNames);
        if (grid is not null)
        {
            GridContextMenuHelper.SelectAll(grid);
            onSelected?.Invoke(grid);
            return;
        }

        GridContextMenuHelper.SelectAllFromMenu(menuItem);
    }

    public static void SelectAllSafe(
        Control host,
        MenuItem? menuItem,
        object? rowItem,
        string[] gridNames,
        string logScope,
        string logEvent,
        string logMessage,
        Action<DataGrid>? onSelected = null)
    {
        try
        {
            SelectAll(host, menuItem, rowItem, gridNames, onSelected);
        }
        catch (Exception ex)
        {
            AppLog.Warn(logScope, logEvent, logMessage, ex);
        }
    }
}
