using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>DataGrid 行级语义着色</summary>
public class DataGridRowTone
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridRowTone, DataGrid, bool>("RowToneEnabled");

    static DataGridRowTone()
    {
        EnabledProperty.Changed.AddClassHandler<DataGrid>((grid, args) =>
            GridToneHost.RowEnabledChanged(grid, args.NewValue is true));
    }

    public static bool GetEnabled(DataGrid grid) => grid.GetValue(EnabledProperty);

    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);
}

internal static class GridToneClass
{
    private static readonly string[] ToneClassNames =
    [
        "ToneWarning10",
        "ToneDanger10",
    ];

    public static string? ToClassName(GridTone tone)
        => tone switch
        {
            GridTone.Warning => "ToneWarning10",
            GridTone.Danger => "ToneDanger10",
            _ => null,
        };

    public static void Apply(DataGridRow row, GridTone tone)
    {
        Clear(row);
        var className = ToClassName(tone);
        if (className is not null)
        {
            row.Classes.Add(className);
        }
    }

    public static void Clear(DataGridRow row)
    {
        foreach (var className in ToneClassNames)
        {
            row.Classes.Remove(className);
        }
    }
}

/// <summary>
/// 行 tone 宿主：LoadingRow 上色，ItemsSource INPC 驱动更新，走 DataGridVisualLifecycle
/// </summary>
internal static class GridToneHost
{
    private sealed class State
    {
        public required EventHandler<DataGridRowEventArgs> LoadingRow { get; init; }
        public required EventHandler<AvaloniaPropertyChangedEventArgs> GridPropertyChanged { get; init; }
        public required DataGrid Grid { get; init; }
        public IEnumerable? ItemsSource { get; set; }
        public NotifyCollectionChangedEventHandler? ItemsChanged { get; set; }
        public Dictionary<INotifyPropertyChanged, PropertyChangedEventHandler> RowSubscriptions { get; } = [];
    }

    private static readonly ConcurrentDictionary<DataGrid, State> States = new();

    internal static void RowEnabledChanged(DataGrid grid, bool enabled)
    {
        if (enabled)
        {
            Attach(grid);
            RefreshMaterializedRows(grid);
            return;
        }

        DetachFully(grid);
    }

    internal static void DetachState(DataGrid grid)
        => DetachState(grid, clearVisual: false);

    internal static void DetachState(DataGrid grid, bool clearVisual)
    {
        if (!States.TryRemove(grid, out var state))
        {
            return;
        }

        grid.LoadingRow -= state.LoadingRow;
        grid.PropertyChanged -= state.GridPropertyChanged;
        DetachItemsSource(state);
        if (clearVisual)
        {
            ClearMaterializedTones(grid);
        }
    }

    private static void Attach(DataGrid grid)
    {
        if (States.ContainsKey(grid))
        {
            return;
        }

        State state = null!;
        state = new State
        {
            Grid = grid,
            LoadingRow = (_, args) => ApplyRow(grid, args.Row),
            GridPropertyChanged = (_, args) =>
            {
                if (args.Property == DataGrid.ItemsSourceProperty)
                {
                    DetachItemsSource(state);
                    AttachItemsSource(state, grid.ItemsSource);
                    RefreshMaterializedRows(grid);
                }
            },
        };

        grid.LoadingRow += state.LoadingRow;
        grid.PropertyChanged += state.GridPropertyChanged;
        AttachItemsSource(state, grid.ItemsSource);
        States[grid] = state;
        DataGridVisualLifecycle.Register(grid, Attach, DetachState, DataGridRowTone.GetEnabled);
    }

    private static void DetachFully(DataGrid grid)
    {
        DetachState(grid, clearVisual: true);
        DataGridVisualLifecycle.Unregister(grid, DetachState);
    }

    private static void AttachItemsSource(State state, IEnumerable? source)
    {
        state.ItemsSource = source;
        if (source is INotifyCollectionChanged collection)
        {
            state.ItemsChanged = (_, args) => OnItemsChanged(state, args);
            collection.CollectionChanged += state.ItemsChanged;
        }

        foreach (var item in Enumerate(source))
        {
            SubscribeRow(state, item);
        }
    }

    private static void DetachItemsSource(State state)
    {
        if (state.ItemsSource is INotifyCollectionChanged collection && state.ItemsChanged is not null)
        {
            collection.CollectionChanged -= state.ItemsChanged;
        }

        foreach (var row in state.RowSubscriptions.Keys.ToArray())
        {
            UnsubscribeRow(state, row);
        }

        state.ItemsSource = null;
        state.ItemsChanged = null;
    }

    private static void OnItemsChanged(State state, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var row in state.RowSubscriptions.Keys.ToArray())
            {
                UnsubscribeRow(state, row);
            }

            foreach (var item in Enumerate(state.ItemsSource))
            {
                SubscribeRow(state, item);
            }

            RefreshMaterializedRows(state.Grid);
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged row)
                {
                    UnsubscribeRow(state, row);
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                SubscribeRow(state, item);
            }
        }
    }

    private static void SubscribeRow(State state, object? item)
    {
        if (item is not INotifyPropertyChanged notify || state.RowSubscriptions.ContainsKey(notify))
        {
            return;
        }

        var grid = state.Grid;
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (!DataGridRowTone.GetEnabled(grid)
                || (args.PropertyName is not null
                    && args.PropertyName != nameof(IRowTone.RowTone)))
            {
                return;
            }

            foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
            {
                if (ReferenceEquals(row.DataContext, notify))
                {
                    ApplyRow(grid, row);
                }
            }
        };

        notify.PropertyChanged += handler;
        state.RowSubscriptions[notify] = handler;
    }

    private static void UnsubscribeRow(State state, INotifyPropertyChanged row)
    {
        if (!state.RowSubscriptions.Remove(row, out var handler))
        {
            return;
        }

        row.PropertyChanged -= handler;
    }

    private static void RefreshMaterializedRows(DataGrid grid)
    {
        foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
        {
            ApplyRow(grid, row);
        }
    }

    private static void ClearMaterializedTones(DataGrid grid)
    {
        foreach (var row in grid.GetVisualDescendants().OfType<DataGridRow>())
        {
            GridToneClass.Clear(row);
        }
    }

    private static void ApplyRow(DataGrid grid, DataGridRow row)
    {
        var tone = DataGridRowTone.GetEnabled(grid) && row.DataContext is IRowTone rowTone
            ? rowTone.RowTone
            : GridTone.None;

        GridToneClass.Apply(row, tone);
    }

    private static IEnumerable<object?> Enumerate(IEnumerable? source)
    {
        if (source is null)
        {
            yield break;
        }

        foreach (var item in source)
        {
            yield return item;
        }
    }
}
