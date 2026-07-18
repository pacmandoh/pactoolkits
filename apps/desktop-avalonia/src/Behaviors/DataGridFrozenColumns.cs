using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.VisualTree;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>
/// Moves marked columns to the left edge before applying Avalonia's contiguous frozen-column range.
/// </summary>
public class DataGridFrozenColumns
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridFrozenColumns, DataGrid, bool>("Enabled");

    public static readonly AttachedProperty<bool> IsFrozenProperty =
        AvaloniaProperty.RegisterAttached<DataGridFrozenColumns, DataGridColumn, bool>("IsFrozen");

    private static readonly ConcurrentDictionary<DataGrid, BehaviorState> States = new();

    static DataGridFrozenColumns()
    {
        EnabledProperty.Changed.AddClassHandler<DataGrid>(OnEnabledChanged);
        IsFrozenProperty.Changed.AddClassHandler<DataGridColumn>((_, _) => RefreshAll());
    }

    public static bool GetEnabled(DataGrid grid) => grid.GetValue(EnabledProperty);

    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);

    public static bool GetIsFrozen(DataGridColumn column) => column.GetValue(IsFrozenProperty);

    public static void SetIsFrozen(DataGridColumn column, bool value) => column.SetValue(IsFrozenProperty, value);

    private static void OnEnabledChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is true)
        {
            Attach(grid);
        }
        else
        {
            DetachFully(grid);
        }
    }

    private static void Attach(DataGrid grid)
    {
        if (States.ContainsKey(grid))
        {
            return;
        }

        var state = new BehaviorState(grid);
        if (!States.TryAdd(grid, state))
        {
            return;
        }

        state.Attach();
        DataGridVisualLifecycle.Register(grid, Attach, DetachState, GetEnabled);
    }

    private static void DetachState(DataGrid grid)
    {
        if (States.TryRemove(grid, out var state))
        {
            state.Dispose();
        }
    }

    private static void DetachFully(DataGrid grid)
    {
        DetachState(grid);
        DataGridVisualLifecycle.Unregister(grid);
    }

    private static void RefreshAll()
    {
        foreach (var state in States.Values)
        {
            state.Apply();
        }
    }

    internal static void Refresh(DataGrid grid)
    {
        if (States.TryGetValue(grid, out var state))
        {
            state.Apply();
        }
    }

    private sealed class BehaviorState(DataGrid grid) : IDisposable
    {
        private bool _applying;
        private bool _disposed;

        public void Attach()
        {
            grid.Columns.CollectionChanged += OnColumnsChanged;
            grid.LayoutUpdated += OnLayoutUpdated;
            Apply();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            grid.Columns.CollectionChanged -= OnColumnsChanged;
            grid.LayoutUpdated -= OnLayoutUpdated;
        }

        public void Apply()
        {
            if (_disposed || _applying)
            {
                return;
            }

            _applying = true;
            try
            {
                var columns = grid.Columns
                    .Select((column, index) => new
                    {
                        Column = column,
                        Order = column.DisplayIndex >= 0 ? column.DisplayIndex : index,
                    })
                    .ToArray();
                var hasFrozenColumns = columns.Any(item =>
                    item.Column.IsVisible && GetIsFrozen(item.Column));
                var frozen = columns
                    .Where(item =>
                        item.Column.IsVisible
                        && (GetIsFrozen(item.Column)
                            || (hasFrozenColumns && DataGridIndexColumn.IsIndexColumn(item.Column))))
                    .OrderBy(item => FrozenPriority(item.Column))
                    .ThenBy(item => item.Order)
                    .Select(item => item.Column)
                    .ToArray();

                if (grid.FrozenColumnCount != 0)
                {
                    grid.FrozenColumnCount = 0;
                }

                for (var index = 0; index < frozen.Length; index++)
                {
                    if (frozen[index].DisplayIndex != index)
                    {
                        frozen[index].DisplayIndex = index;
                    }
                }

                grid.FrozenColumnCount = frozen.Length;
            }
            finally
            {
                _applying = false;
            }
        }

        private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => Apply();

        private void OnLayoutUpdated(object? sender, EventArgs e)
            => AlignFrozenRowsToViewport(grid);

        private static int FrozenPriority(DataGridColumn column)
        {
            if (DataGridIndexColumn.IsIndexColumn(column))
            {
                return 0;
            }

            return DataGridRowSelection.IsSelectionColumn(column) ? 1 : 2;
        }
    }

    internal static void AlignFrozenRowsToViewport(DataGrid grid)
    {
        if (grid.FrozenColumnCount == 0)
        {
            return;
        }

        foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
        {
            var cellsPresenter = row.GetVisualDescendants()
                .OfType<DataGridCellsPresenter>()
                .FirstOrDefault();
            if (cellsPresenter?.RenderTransform is not TranslateTransform transform)
            {
                continue;
            }

            // Avalonia rounds the row bounds to physical pixels but rounds this transform to whole DIPs.
            var alignedOffset = -row.Bounds.X;
            if (Math.Abs(transform.X - alignedOffset) > 0.001)
            {
                transform.X = alignedOffset;
            }
        }
    }
}
