using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// 可选单元格选中：保留行背景，:current 格再深一档；Cmd/Ctrl+C 复制当前格文本；默认关闭（原生行选中）
/// </summary>
public class DataGridCellSelect
{
    private const string StyleClass = "CellSelect";

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridCellSelect, DataGrid, bool>("CellSelectEnabled");

    private sealed class State
    {
        public EventHandler<KeyEventArgs>? KeyDown;
        public EventHandler<DataGridCellPointerPressedEventArgs>? CellPointerPressed;
        public EventHandler<EventArgs>? CurrentCellChanged;
        public bool ClearingChromeCurrency;
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
            CurrentCellChanged = (_, _) => ClearCurrencyIfChrome(grid),
        };
        if (!States.TryAdd(grid, state))
        {
            return;
        }

        grid.Classes.Add(StyleClass);
        grid.AddHandler(InputElement.KeyDownEvent, state.KeyDown, RoutingStrategies.Tunnel);
        grid.CellPointerPressed += state.CellPointerPressed;
        grid.CurrentCellChanged += state.CurrentCellChanged;
        ClearCurrencyIfChrome(grid);
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

        if (state.CurrentCellChanged is not null)
        {
            grid.CurrentCellChanged -= state.CurrentCellChanged;
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

        // # / 勾选列不参与单元格选中
        Dispatcher.UIThread.Post(() => ClearCurrencyIfChrome(grid), DispatcherPriority.Background);
    }

    private static void ClearCurrencyIfChrome(DataGrid grid)
    {
        if (!GetEnabled(grid) || !States.TryGetValue(grid, out var state) || state.ClearingChromeCurrency)
        {
            return;
        }

        if (!IsChromeColumn(grid.CurrentColumn))
        {
            return;
        }

        state.ClearingChromeCurrency = true;
        try
        {
            DataGridInteractionHelper.ClearCurrency(grid);
        }
        finally
        {
            state.ClearingChromeCurrency = false;
        }
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

        if (IsChromeColumn(grid.CurrentColumn))
        {
            return;
        }

        e.Handled = true;
        _ = CopyCurrentCellAsync(grid);
    }

    private static async System.Threading.Tasks.Task CopyCurrentCellAsync(DataGrid grid)
    {
        try
        {
            var text = TryGetCurrentCellText(grid);
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
            AppLog.Warn("DataGridCellSelect", "grid.cell_copy.fail", "Failed to copy current cell", ex);
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
