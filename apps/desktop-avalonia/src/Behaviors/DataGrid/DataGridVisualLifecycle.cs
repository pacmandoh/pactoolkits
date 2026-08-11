using System;
using System.Collections.Generic;
using Avalonia;
using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// <see cref="DataGrid"/> 离开 visual tree 时丢弃 attached behavior 状态；
/// 仍启用时回到树中再重新 attach
/// </summary>
internal static class DataGridVisualLifecycle
{
    private sealed class Entry
    {
        public required Action<DataGrid> Attach { get; init; }
        public required Action<DataGrid> DetachState { get; init; }
        public required Func<DataGrid, bool> IsEnabled { get; init; }
    }

    private sealed class Hooks
    {
        public required EventHandler<VisualTreeAttachmentEventArgs> Detached { get; init; }
        public required EventHandler<VisualTreeAttachmentEventArgs> Attached { get; init; }
        public List<Entry> Entries { get; } = [];
    }

    private static readonly AttachedProperty<Hooks?> RegisteredProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, Hooks?>("VisualLifecycleHooks", typeof(DataGridVisualLifecycle));

    internal static void Register(
        DataGrid grid,
        Action<DataGrid> attach,
        Action<DataGrid> detachState,
        Func<DataGrid, bool> isEnabled)
    {
        var hooks = grid.GetValue(RegisteredProperty);
        if (hooks is null)
        {
            hooks = CreateHooks(grid);
            grid.SetValue(RegisteredProperty, hooks);
            grid.DetachedFromVisualTree += hooks.Detached;
            grid.AttachedToVisualTree += hooks.Attached;
        }

        foreach (var entry in hooks.Entries)
        {
            if (ReferenceEquals(entry.DetachState, detachState))
            {
                return;
            }
        }

        hooks.Entries.Add(new Entry
        {
            Attach = attach,
            DetachState = detachState,
            IsEnabled = isEnabled,
        });
    }

    internal static void Unregister(DataGrid grid, Action<DataGrid> detachState)
    {
        if (grid.GetValue(RegisteredProperty) is not Hooks hooks)
        {
            return;
        }

        hooks.Entries.RemoveAll(entry => ReferenceEquals(entry.DetachState, detachState));
        if (hooks.Entries.Count > 0)
        {
            return;
        }

        grid.ClearValue(RegisteredProperty);
        grid.DetachedFromVisualTree -= hooks.Detached;
        grid.AttachedToVisualTree -= hooks.Attached;
    }

    internal static bool HasHooks(DataGrid grid) => grid.GetValue(RegisteredProperty) is { Entries.Count: > 0 };

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

    private static Hooks CreateHooks(DataGrid grid)
    {
        EventHandler<VisualTreeAttachmentEventArgs> detached = (sender, _) =>
        {
            if (!ReferenceEquals(sender, grid))
            {
                return;
            }

            // 快照：detach 可能 Unregister 自己；拆树期间勿再改 Columns（Avalonia #13497）
            var entries = grid.GetValue(RegisteredProperty)?.Entries.ToArray() ?? Array.Empty<Entry>();
            foreach (var entry in entries)
            {
                entry.DetachState(grid);
            }
        };

        EventHandler<VisualTreeAttachmentEventArgs> attached = (sender, _) =>
        {
            if (!ReferenceEquals(sender, grid))
            {
                return;
            }

            var entries = grid.GetValue(RegisteredProperty)?.Entries.ToArray() ?? Array.Empty<Entry>();
            foreach (var entry in entries)
            {
                if (entry.IsEnabled(grid))
                {
                    entry.Attach(grid);
                }
            }
        };

        return new Hooks
        {
            Detached = detached,
            Attached = attached,
        };
    }
}
