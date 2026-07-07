using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Data;
using global::Avalonia.Controls;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// Avalonia 12 compiled bindings omit sort paths on some bound text columns unless seeded here.
/// </summary>
public class DataGridSortSupport
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridSortSupport, DataGrid, bool>("Enabled");

    private static readonly ConcurrentDictionary<DataGrid, NotifyCollectionChangedEventHandler> ColumnHandlers = new();

    static DataGridSortSupport()
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
        if (ColumnHandlers.ContainsKey(grid))
        {
            return;
        }

        NotifyCollectionChangedEventHandler handler = (_, _) => Apply(grid);
        if (!ColumnHandlers.TryAdd(grid, handler))
        {
            return;
        }

        grid.AttachedToVisualTree += OnAttachedToVisualTree;
        grid.Columns.CollectionChanged += handler;
        Apply(grid);
        DataGridVisualLifecycle.Register(grid, Attach, DetachState, GetEnabled);
    }

    private static void DetachState(DataGrid grid)
    {
        if (!ColumnHandlers.TryRemove(grid, out var handler))
        {
            return;
        }

        grid.AttachedToVisualTree -= OnAttachedToVisualTree;
        grid.Columns.CollectionChanged -= handler;
    }

    private static void DetachFully(DataGrid grid)
    {
        DetachState(grid);
        DataGridVisualLifecycle.Unregister(grid);
    }

    private static void OnAttachedToVisualTree(object? sender, EventArgs e)
    {
        if (sender is DataGrid grid)
        {
            Dispatcher.UIThread.Post(() => Apply(grid), DispatcherPriority.Loaded);
        }
    }

    internal static void Apply(DataGrid grid)
    {
        grid.CanUserSortColumns = true;

        foreach (var column in grid.Columns)
        {
            if (column is not DataGridBoundColumn boundColumn)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(column.SortMemberPath))
            {
                var path = GetBindingPath(boundColumn.Binding);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    column.SortMemberPath = path;
                }
            }
        }
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
}
