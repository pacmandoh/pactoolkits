using System;
using Avalonia;
using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// Drops attached behavior state when a <see cref="DataGrid"/> leaves the visual tree
/// and re-attaches when it returns while still enabled.
/// </summary>
internal static class DataGridVisualLifecycle
{
    private sealed class Hooks
    {
        public required EventHandler<VisualTreeAttachmentEventArgs> Detached { get; init; }
        public required EventHandler<VisualTreeAttachmentEventArgs> Attached { get; init; }
    }

    private static readonly AttachedProperty<Hooks?> RegisteredProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, Hooks?>("VisualLifecycleHooks", typeof(DataGridVisualLifecycle));

    internal static void Register(
        DataGrid grid,
        Action<DataGrid> attach,
        Action<DataGrid> detachState,
        Func<DataGrid, bool> isEnabled)
    {
        if (grid.GetValue(RegisteredProperty) is not null)
        {
            return;
        }

        var hooks = CreateHooks(grid, attach, detachState, isEnabled);
        grid.SetValue(RegisteredProperty, hooks);
        grid.DetachedFromVisualTree += hooks.Detached;
        grid.AttachedToVisualTree += hooks.Attached;
    }

    internal static void Unregister(DataGrid grid)
    {
        if (grid.GetValue(RegisteredProperty) is not Hooks hooks)
        {
            return;
        }

        grid.ClearValue(RegisteredProperty);
        grid.DetachedFromVisualTree -= hooks.Detached;
        grid.AttachedToVisualTree -= hooks.Attached;
    }

    internal static bool HasHooks(DataGrid grid) => grid.GetValue(RegisteredProperty) is not null;

    internal static void InvokeDetached(DataGrid grid)
    {
        if (grid.GetValue(RegisteredProperty) is Hooks hooks)
        {
            hooks.Detached(grid, null!);
        }
    }

    internal static void InvokeAttached(DataGrid grid)
    {
        if (grid.GetValue(RegisteredProperty) is Hooks hooks)
        {
            hooks.Attached(grid, null!);
        }
    }

    private static Hooks CreateHooks(
        DataGrid grid,
        Action<DataGrid> attach,
        Action<DataGrid> detachState,
        Func<DataGrid, bool> isEnabled)
    {
        EventHandler<VisualTreeAttachmentEventArgs> detached = (sender, _) =>
        {
            if (ReferenceEquals(sender, grid))
            {
                detachState(grid);
            }
        };

        EventHandler<VisualTreeAttachmentEventArgs> attached = (sender, _) =>
        {
            if (ReferenceEquals(sender, grid) && isEnabled(grid))
            {
                attach(grid);
            }
        };

        return new Hooks
        {
            Detached = detached,
            Attached = attached,
        };
    }
}
