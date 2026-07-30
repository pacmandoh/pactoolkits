using System.Reflection;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Behaviors;

namespace PacToolkits.Desktop.UiTests;

public sealed class DataGridBehaviorLifecycleTests
{
    [AvaloniaFact]
    public void Visual_detach_drops_display_only_state_while_still_enabled()
    {
        var grid = new DataGrid();
        DataGridDisplayOnly.SetEnabled(grid, true);

        Assert.True(HasBehaviorState<DataGridDisplayOnly>(grid));
        Assert.True(DataGridVisualLifecycle.HasHooks(grid));

        DataGridVisualLifecycle.InvokeDetached(grid);

        Assert.False(HasBehaviorState<DataGridDisplayOnly>(grid));
        Assert.True(DataGridDisplayOnly.GetEnabled(grid));
        Assert.True(DataGridVisualLifecycle.HasHooks(grid));
    }

    [AvaloniaFact]
    public void Visual_reattach_restores_display_only_state()
    {
        var grid = new DataGrid();
        DataGridDisplayOnly.SetEnabled(grid, true);
        DataGridVisualLifecycle.InvokeDetached(grid);

        DataGridVisualLifecycle.InvokeAttached(grid);

        Assert.True(HasBehaviorState<DataGridDisplayOnly>(grid));
    }

    [AvaloniaFact]
    public void Disable_unregisters_visual_lifecycle_hooks()
    {
        var grid = new DataGrid();
        DataGridDisplayOnly.SetEnabled(grid, true);
        DataGridDisplayOnly.SetEnabled(grid, false);

        Assert.False(HasBehaviorState<DataGridDisplayOnly>(grid));
        Assert.False(DataGridVisualLifecycle.HasHooks(grid));
    }

    [AvaloniaFact]
    public void Visual_detach_drops_index_column_state()
        => AssertVisualDetachDropsBehaviorState(typeof(DataGridIndexColumn), nameof(DataGridIndexColumn.SetEnabled));

    [AvaloniaFact]
    public void Visual_detach_drops_row_selection_state()
        => AssertVisualDetachDropsBehaviorState(typeof(DataGridRowSelection), nameof(DataGridRowSelection.SetEnabled));

    [AvaloniaFact]
    public void Visual_detach_drops_sort_reset_state()
        => AssertVisualDetachDropsBehaviorState(typeof(DataGridSortReset), nameof(DataGridSortReset.SetEnabled));

    [AvaloniaFact]
    public void Visual_detach_drops_sort_support_state()
        => AssertVisualDetachDropsBehaviorState(typeof(DataGridSortSupport), nameof(DataGridSortSupport.SetEnabled));

    [AvaloniaFact]
    public void Multiple_behaviors_share_visual_lifecycle_and_all_detach()
    {
        var grid = new DataGrid();
        DataGridDisplayOnly.SetEnabled(grid, true);
        DataGridSortSupport.SetEnabled(grid, true);

        Assert.True(DataGridVisualLifecycle.HasHooks(grid));
        Assert.True(HasBehaviorState<DataGridDisplayOnly>(grid));
        Assert.True(HasBehaviorState(typeof(DataGridSortSupport), grid));

        DataGridVisualLifecycle.InvokeDetached(grid);

        Assert.False(HasBehaviorState<DataGridDisplayOnly>(grid));
        Assert.False(HasBehaviorState(typeof(DataGridSortSupport), grid));
        Assert.True(DataGridVisualLifecycle.HasHooks(grid));

        DataGridVisualLifecycle.InvokeAttached(grid);

        Assert.True(HasBehaviorState<DataGridDisplayOnly>(grid));
        Assert.True(HasBehaviorState(typeof(DataGridSortSupport), grid));
    }

    [AvaloniaFact]
    public void Visual_detach_keeps_row_selection_column()
    {
        var grid = new DataGrid();
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Width = new DataGridLength(120) });
        DataGridRowSelection.SetEnabled(grid, true);

        var before = grid.Columns.Count;
        Assert.True(before >= 2);

        DataGridVisualLifecycle.InvokeDetached(grid);

        Assert.Equal(before, grid.Columns.Count);
        Assert.Contains(grid.Columns, DataGridRowSelection.IsSelectionColumn);
    }

    private static void AssertVisualDetachDropsBehaviorState(Type behaviorType, string setEnabledName)
    {
        var grid = new DataGrid();
        var setEnabled = behaviorType.GetMethod(
            setEnabledName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(DataGrid), typeof(bool)],
            modifiers: null);
        Assert.NotNull(setEnabled);

        setEnabled!.Invoke(null, [grid, true]);
        Assert.True(HasBehaviorState(behaviorType, grid));

        DataGridVisualLifecycle.InvokeDetached(grid);

        Assert.False(HasBehaviorState(behaviorType, grid));
        Assert.True(DataGridVisualLifecycle.HasHooks(grid));
    }

    private static bool HasBehaviorState<TBehavior>(DataGrid grid)
        => HasBehaviorState(typeof(TBehavior), grid);

    private static bool HasBehaviorState(Type behaviorType, DataGrid grid)
    {
        var field = behaviorType.GetField("States", BindingFlags.NonPublic | BindingFlags.Static)
            ?? behaviorType.GetField("ColumnHandlers", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var dictionary = field!.GetValue(null);
        Assert.NotNull(dictionary);

        var contains = dictionary!.GetType().GetMethod("ContainsKey", [typeof(DataGrid)]);
        Assert.NotNull(contains);

        return (bool)contains!.Invoke(dictionary, [grid])!;
    }
}
