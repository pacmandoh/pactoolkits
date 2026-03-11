using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.VisualTree;
using pactoolkits_ui.Services.Infrastructure;

namespace pactoolkits_ui.Common;

public static class GridContextMenuHelper
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> PropertyCache = new();

    public static async Task CopyRowAsTextAsync(
        IClipboardService clipboard,
        DataGrid? grid,
        object? rowItem,
        params string[] preferredProps)
    {
        var target = rowItem ?? grid?.SelectedItem;
        await CopyRowAsTextAsync(clipboard, target, preferredProps);
    }

    public static async Task CopyRowAsTextAsync(
        IClipboardService clipboard,
        MenuItem? menuItem,
        object? rowItem,
        params string[] preferredProps)
    {
        var target = rowItem ?? FindOwnerGrid(menuItem)?.SelectedItem;
        await CopyRowAsTextAsync(clipboard, target, preferredProps);
    }

    public static async Task CopyRowAsTextAsync(IClipboardService clipboard, object? rowItem, params string[] preferredProps)
    {
        if (rowItem is null)
            return;

        var text = BuildRowText(rowItem, preferredProps);
        if (string.IsNullOrWhiteSpace(text))
            return;

        await clipboard.SetTextAsync(text);
    }

    public static async Task CopyRowsAsTextAsync(
        IClipboardService clipboard,
        IEnumerable<object?> rows,
        params string[] preferredProps)
    {
        var lines = rows
            .Where(r => r is not null)
            .Select(r => BuildRowText(r!, preferredProps))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (lines.Count == 0)
            return;

        await clipboard.SetTextAsync(string.Join(Environment.NewLine, lines));
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
            return null;

        var cm = menuItem.FindAncestorOfType<ContextMenu>();
        var target = cm?.PlacementTarget;
        if (target is null)
            return null;

        if (target is DataGrid dg)
            return dg;

        if (target is { } control)
            return control.FindAncestorOfType<DataGrid>();

        return null;
    }

    private static string BuildRowText(object rowItem, IReadOnlyList<string> preferredProps)
    {
        var t = rowItem.GetType();
        var values = new List<string>(4);

        foreach (var name in preferredProps)
        {
            var p = ResolveProperty(t, name);
            var v = p?.GetValue(rowItem)?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(v))
                values.Add(v);
        }

        if (values.Count > 0)
            return string.Join(" / ", values);

        foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!p.CanRead) continue;
            if (!IsSimpleType(p.PropertyType)) continue;

            var v = p.GetValue(rowItem)?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(v))
                values.Add(v);

            if (values.Count >= 4)
                break;
        }

        return string.Join(" / ", values);
    }

    private static PropertyInfo? ResolveProperty(Type type, string name)
        => PropertyCache.GetOrAdd(
            (type, name),
            static key => key.Type.GetProperty(
                key.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase));

    private static bool IsSimpleType(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u.IsPrimitive ||
               u.IsEnum ||
               u == typeof(string) ||
               u == typeof(decimal) ||
               u == typeof(DateTime) ||
               u == typeof(DateTimeOffset) ||
               u == typeof(Guid);
    }
}
