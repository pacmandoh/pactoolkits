using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Data;
using global::Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class GridContextMenuHelper
{
    private sealed record ExportColumn(string Header, string PropertyPath);

    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> PropertyCache = new();

    public static async Task CopyRowAsTextAsync(
        IClipboardService clipboard,
        DataGrid? grid,
        object? rowItem,
        params string[] preferredProps)
    {
        var target = rowItem ?? grid?.SelectedItem;
        await CopyRowsAsTextAsync(clipboard, grid, target is null ? [] : [target], preferredProps);
    }

    public static async Task CopyRowAsTextAsync(
        IClipboardService clipboard,
        MenuItem? menuItem,
        object? rowItem,
        params string[] preferredProps)
    {
        var grid = FindOwnerGrid(menuItem);
        var target = rowItem ?? grid?.SelectedItem;
        await CopyRowsAsTextAsync(clipboard, grid, target is null ? [] : [target], preferredProps);
    }

    public static async Task CopyRowAsTextAsync(IClipboardService clipboard, object? rowItem, params string[] preferredProps)
        => await CopyRowsAsTextAsync(clipboard, null, rowItem is null ? [] : [rowItem], preferredProps);

    public static async Task CopyRowsAsTextAsync(
        IClipboardService clipboard,
        IEnumerable<object?> rows,
        params string[] preferredProps)
        => await CopyRowsAsTextAsync(clipboard, null, rows, preferredProps);

    public static async Task CopyRowsAsTextAsync(
        IClipboardService clipboard,
        DataGrid? grid,
        IEnumerable<object?> rows,
        params string[] preferredProps)
    {
        var materialized = rows.Where(static r => r is not null).Cast<object>().ToList();
        if (materialized.Count == 0)
        {
            return;
        }

        var csv = grid is not null
            ? BuildCsvFromGrid(grid, materialized)
            : BuildCsvFromPreferredProps(materialized, preferredProps);

        if (string.IsNullOrWhiteSpace(csv))
        {
            return;
        }

        await clipboard.SetTextAsync(csv);
    }

    public static void SelectAllFromMenu(MenuItem? menuItem)
    {
        var grid = FindOwnerGrid(menuItem);
        grid?.SelectAll();
    }

    public static void SelectAll(DataGrid? grid)
    {
        grid?.SelectAll();
    }

    public static DataGrid? FindOwnerGrid(MenuItem? menuItem)
    {
        if (menuItem is null)
        {
            return null;
        }

        var cm = menuItem.FindAncestorOfType<ContextMenu>();
        var target = cm?.PlacementTarget;
        if (target is null)
        {
            return null;
        }

        if (target is DataGrid dg)
        {
            return dg;
        }

        if (target is { } control)
        {
            return control.FindAncestorOfType<DataGrid>();
        }

        return null;
    }

    private static string? BuildCsvFromGrid(DataGrid grid, IReadOnlyList<object> rows)
    {
        var columns = ResolveExportColumns(grid);
        return columns.Count == 0 ? null : BuildCsv(columns, rows);
    }

    private static string? BuildCsvFromPreferredProps(IReadOnlyList<object> rows, IReadOnlyList<string> preferredProps)
    {
        if (preferredProps.Count == 0)
        {
            return null;
        }

        var columns = preferredProps
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => new ExportColumn(name, name))
            .ToList();

        return columns.Count == 0 ? null : BuildCsv(columns, rows);
    }

    private static List<ExportColumn> ResolveExportColumns(DataGrid grid)
    {
        var columns = new List<ExportColumn>(grid.Columns.Count);
        foreach (var column in grid.Columns)
        {
            if (column.IsVisible == false)
            {
                continue;
            }

            var header = column.Header?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var path = ResolveBindingPath(column);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            columns.Add(new ExportColumn(header, path));
        }

        return columns;
    }

    private static string? ResolveBindingPath(DataGridColumn column)
    {
        if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
        {
            return column.SortMemberPath;
        }

        if (column is DataGridBoundColumn boundColumn)
        {
            return GetBindingPath(boundColumn.Binding);
        }

        return null;
    }

    private static string? GetBindingPath(BindingBase? binding)
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

    private static string BuildCsv(IReadOnlyList<ExportColumn> columns, IReadOnlyList<object> rows)
    {
        var sb = new StringBuilder(Math.Max(256, columns.Count * rows.Count * 16));
        AppendCsvRow(sb, columns.Select(static c => c.Header));

        foreach (var row in rows)
        {
            sb.AppendLine();
            AppendCsvRow(
                sb,
                columns.Select(column => GetPropertyValue(row, column.PropertyPath)));
        }

        return sb.ToString();
    }

    private static void AppendCsvRow(StringBuilder sb, IEnumerable<string?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                sb.Append(',');
            }

            sb.Append(EscapeCsvField(value));
            first = false;
        }
    }

    private static string EscapeCsvField(string? value)
    {
        var text = value ?? string.Empty;
        if (text.IndexOfAny([',', '"', '\r', '\n']) >= 0)
        {
            return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        return text;
    }

    private static string? GetPropertyValue(object rowItem, string propertyPath)
    {
        var p = ResolveProperty(rowItem.GetType(), propertyPath);
        var raw = p?.GetValue(rowItem);
        return raw switch
        {
            null => string.Empty,
            IFormattable formattable when raw is not string => formattable.ToString(null, null),
            _ => raw.ToString()
        };
    }

    private static PropertyInfo? ResolveProperty(Type type, string name)
        => PropertyCache.GetOrAdd(
            (type, name),
            static key => key.Type.GetProperty(
                key.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase));
}
