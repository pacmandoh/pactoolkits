using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Diagnostics;
using PacToolkits.Desktop.Avalonia.Ui.Interaction;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// 可选单元格选中：数据格 :current 再深一档；# / 勾选列用 DgChrome 压住 :current；# 点选整行；Cmd/Ctrl+C 复制格或整行；默认关闭
/// </summary>
public class DataGridCellSelect
{
    private const string StyleClass = "CellSelect";

    /// <summary># / 勾选列 CellStyleClasses：CellSelect 下不显示 :current</summary>
    public const string ChromeCellClass = "DgChrome";

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridCellSelect, DataGrid, bool>("CellSelectEnabled");

    private sealed class State
    {
        public EventHandler<KeyEventArgs>? KeyDown;
        public EventHandler<DataGridCellPointerPressedEventArgs>? CellPointerPressed;
    }

    private static readonly ConcurrentDictionary<DataGrid, State> States = new();

    static DataGridCellSelect()
    {
        EnabledProperty.Changed.AddClassHandler<DataGrid>((grid, args) =>
        {
            var enabled = args.NewValue is bool b && b;
            if (enabled)
            {
                Attach(grid);
            }
            else
            {
                DetachFully(grid);
            }
        });
    }

    public static bool GetEnabled(DataGrid grid) => grid.GetValue(EnabledProperty);

    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);

    internal static bool IsChromeColumn(DataGridColumn? column)
        => DataGridIndexColumn.IsIndexColumn(column) || DataGridRowSelection.IsSelectionColumn(column);

    private static void Attach(DataGrid grid)
    {
        if (States.ContainsKey(grid))
        {
            return;
        }

        var state = new State
        {
            KeyDown = OnKeyDown,
            CellPointerPressed = OnCellPointerPressed,
        };
        if (!States.TryAdd(grid, state))
        {
            return;
        }

        grid.Classes.Add(StyleClass);
        grid.AddHandler(InputElement.KeyDownEvent, state.KeyDown, RoutingStrategies.Tunnel);
        grid.CellPointerPressed += state.CellPointerPressed;
        DataGridVisualLifecycle.Register(grid, Attach, DetachState, GetEnabled);
    }

    private static void DetachState(DataGrid grid)
        => DetachState(grid, removeStyleClass: false);

    private static void DetachState(DataGrid grid, bool removeStyleClass)
    {
        if (!States.TryRemove(grid, out var state))
        {
            return;
        }

        if (state.KeyDown is not null)
        {
            grid.RemoveHandler(InputElement.KeyDownEvent, state.KeyDown);
        }

        if (state.CellPointerPressed is not null)
        {
            grid.CellPointerPressed -= state.CellPointerPressed;
        }

        // visual detach 期间改 Classes 可能牵动模板子树；禁用时再去掉 CellSelect
        if (removeStyleClass)
        {
            grid.Classes.Remove(StyleClass);
        }
    }

    private static void DetachFully(DataGrid grid)
    {
        DetachState(grid, removeStyleClass: true);
        DataGridVisualLifecycle.Unregister(grid, DetachState);
    }

    private static void OnCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        if (sender is not DataGrid grid || !GetEnabled(grid) || !IsChromeColumn(e.Column))
        {
            return;
        }

        // DataGridCell 在 OnCellPointerPressed 之后才 UpdateStateOnMouse*；Handled 后不会把 :current 落到 chrome
        e.PointerPressedEventArgs.Handled = true;

        if (!DataGridIndexColumn.IsIndexColumn(e.Column))
        {
            return;
        }

        var item = e.Row?.DataContext;
        if (item is not null)
        {
            try
            {
                grid.SelectedItem = item;
            }
            catch (Exception ex)
            {
                AppLog.Warn("DataGridCellSelect", "grid.row_select.fail", "Failed to select row from index", ex);
            }
        }

        // 整行选中：清掉数据格 :current（MakeFirst 可能再落到 #，DgChrome 无视觉）
        DataGridInteractionHelper.ClearCurrency(grid);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not DataGrid grid || !GetEnabled(grid) || e.Handled)
        {
            return;
        }

        if (e.Key != Key.C)
        {
            return;
        }

        var mods = e.KeyModifiers;
        var ctrlOrCmd = mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta);
        if (!ctrlOrCmd || mods.HasFlag(KeyModifiers.Shift) || mods.HasFlag(KeyModifiers.Alt))
        {
            return;
        }

        // 数据列 currency 时复制格；否则仅在有 SelectedItem 时复制整行（初始化 # 幽灵 currency 且无行选中则不复制）
        if (grid.CurrentColumn is not null && !IsChromeColumn(grid.CurrentColumn))
        {
            e.Handled = true;
            _ = CopyTextAsync(grid, TryGetCurrentCellText(grid), "grid.cell_copy.fail", "Failed to copy current cell");
            return;
        }

        if (grid.SelectedItem is null)
        {
            return;
        }

        e.Handled = true;
        _ = CopyTextAsync(grid, TryGetSelectedRowText(grid), "grid.row_copy.fail", "Failed to copy selected row");
    }

    private static async System.Threading.Tasks.Task CopyTextAsync(
        DataGrid grid,
        string? text,
        string eventName,
        string message)
    {
        try
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(grid);
            if (topLevel?.Clipboard is null)
            {
                return;
            }

            await topLevel.Clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            AppLog.Warn("DataGridCellSelect", eventName, message, ex);
        }
    }

    internal static string? TryGetCurrentCellText(DataGrid grid)
    {
        var column = grid.CurrentColumn;
        var item = grid.SelectedItem;
        if (column is null || item is null || IsChromeColumn(column))
        {
            return null;
        }

        return TryGetCellText(column, item);
    }

    internal static string? TryGetSelectedRowText(DataGrid grid)
    {
        var item = grid.SelectedItem;
        if (item is null)
        {
            return null;
        }

        return TryGetRowText(grid, item);
    }

    internal static string? TryGetRowText(DataGrid grid, object item)
    {
        var parts = new List<string>();
        foreach (var column in grid.Columns.OrderBy(static c => c.DisplayIndex))
        {
            if (!column.IsVisible || IsChromeColumn(column))
            {
                continue;
            }

            parts.Add(TryGetCellText(column, item) ?? string.Empty);
        }

        return parts.Count == 0 ? null : string.Join('\t', parts);
    }

    private static string? TryGetCellText(DataGridColumn column, object item)
    {
        try
        {
            var content = column.GetCellContent(item);
            var text = ExtractText(content);
            if (!string.IsNullOrEmpty(text))
            {
                return text;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("DataGridCellSelect", "grid.cell_content.fail", "Failed reading cell content", ex);
        }

        if (column is DataGridBoundColumn bound
            && DataGridInteractionHelper.Rules.SortPath(bound.Binding) is { } path
            && !string.IsNullOrWhiteSpace(path))
        {
            return ResolvePath(item, path)?.ToString();
        }

        return null;
    }

    private static string? ExtractText(Control? content)
    {
        if (content is null)
        {
            return null;
        }

        switch (content)
        {
            case TextBlock textBlock:
                return textBlock.Text;
            case TextBox textBox:
                return textBox.Text;
            case ContentControl { Content: string s }:
                return s;
            case ContentControl { Content: Control nested }:
                return ExtractText(nested);
        }

        var parts = content.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(static tb => tb.Text)
            .Where(static t => !string.IsNullOrEmpty(t))
            .ToArray();
        if (parts.Length == 0)
        {
            return null;
        }

        if (parts.Length == 1)
        {
            return parts[0];
        }

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(part);
        }

        return sb.ToString();
    }

    private static object? ResolvePath(object? source, string path)
    {
        var current = source;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null)
            {
                return null;
            }

            var prop = current.GetType().GetProperty(segment);
            if (prop is null)
            {
                return null;
            }

            current = prop.GetValue(current);
        }

        return current;
    }
}
