using System.Reflection;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Behaviors;

namespace PacToolkits.Desktop.Tests;

public sealed class DataGridBehaviorLifecycleTests
{
    [Fact]
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

    [Fact]
    public void Visual_reattach_restores_display_only_state()
    {
        var grid = new DataGrid();
        DataGridDisplayOnly.SetEnabled(grid, true);
        DataGridVisualLifecycle.InvokeDetached(grid);

        DataGridVisualLifecycle.InvokeAttached(grid);

        Assert.True(HasBehaviorState<DataGridDisplayOnly>(grid));
    }

    [Fact]
    public void Disable_unregisters_visual_lifecycle_hooks()
    {
        var grid = new DataGrid();
        DataGridDisplayOnly.SetEnabled(grid, true);
        DataGridDisplayOnly.SetEnabled(grid, false);

        Assert.False(HasBehaviorState<DataGridDisplayOnly>(grid));
        Assert.False(DataGridVisualLifecycle.HasHooks(grid));
    }

    [Theory]
    [InlineData(typeof(DataGridIndexColumn), nameof(DataGridIndexColumn.SetEnabled))]
    [InlineData(typeof(DataGridRowSelection), nameof(DataGridRowSelection.SetEnabled))]
    [InlineData(typeof(DataGridSortReset), nameof(DataGridSortReset.SetEnabled))]
    [InlineData(typeof(DataGridSortSupport), nameof(DataGridSortSupport.SetEnabled))]
    public void Visual_detach_drops_behavior_state(Type behaviorType, string setEnabledName)
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
