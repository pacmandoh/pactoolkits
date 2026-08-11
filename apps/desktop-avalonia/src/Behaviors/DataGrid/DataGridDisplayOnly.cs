using System;
using System.Collections.Concurrent;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Ui.Interaction;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// 只读展示 DataGrid：禁止行/单元格选中，滚动与滚动条仍可用
/// </summary>
public class DataGridDisplayOnly
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridDisplayOnly, DataGrid, bool>("Enabled");

    private sealed class State
    {
        public bool ClearingSelection;
        public EventHandler<SelectionChangedEventArgs>? SelectionChanged;
        public EventHandler<DataGridCellPointerPressedEventArgs>? CellPointerPressed;
    }

    private static readonly ConcurrentDictionary<DataGrid, State> States = new();

    static DataGridDisplayOnly()
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

    private static void Attach(DataGrid grid)
    {
        if (States.ContainsKey(grid))
        {
            return;
        }

        var state = new State();
        state.SelectionChanged = (_, _) => ClearSelection(grid, state);
        // Background Post：避开选中事件重入
        state.CellPointerPressed = (_, _) =>
            Dispatcher.UIThread.Post(() => ClearSelection(grid, state), DispatcherPriority.Background);

        grid.SelectionChanged += state.SelectionChanged;
        grid.CellPointerPressed += state.CellPointerPressed;
        States[grid] = state;
        ClearSelection(grid, state);
        DataGridVisualLifecycle.Register(grid, Attach, DetachState, GetEnabled);
    }

    private static void DetachState(DataGrid grid)
    {
        if (!States.TryRemove(grid, out var state))
        {
            return;
        }

        if (state.SelectionChanged is not null)
        {
            grid.SelectionChanged -= state.SelectionChanged;
        }

        if (state.CellPointerPressed is not null)
        {
            grid.CellPointerPressed -= state.CellPointerPressed;
        }
    }

    private static void DetachFully(DataGrid grid)
    {
        DetachState(grid);
        DataGridVisualLifecycle.Unregister(grid, DetachState);
    }

    private static void ClearSelection(DataGrid grid, State state)
    {
        if (state.ClearingSelection)
        {
            return;
        }

        state.ClearingSelection = true;
        try
        {
            DataGridInteractionHelper.ClearSelection(grid);
        }
        finally
        {
            state.ClearingSelection = false;
        }
    }
}
