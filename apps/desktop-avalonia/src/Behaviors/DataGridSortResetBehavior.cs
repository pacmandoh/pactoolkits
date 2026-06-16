using System;
using System.Collections.Concurrent;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

public class DataGridSortResetBehavior
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridSortResetBehavior, DataGrid, bool>("Enabled");

    private static readonly ConcurrentDictionary<DataGrid, BehaviorState> States = new();

    static DataGridSortResetBehavior()
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
                Detach(grid);
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

        var state = new BehaviorState(grid);
        if (!States.TryAdd(grid, state))
        {
            return;
        }

        state.Attach();
    }

    private static void Detach(DataGrid grid)
    {
        if (!States.TryRemove(grid, out var state))
        {
            return;
        }

        state.Dispose();
    }

    private sealed class BehaviorState : IDisposable
    {
        private readonly DataGrid _grid;
        private readonly Button _button;
        private bool _disposed;

        public BehaviorState(DataGrid grid)
        {
            _grid = grid;
            _button = BuildButton();
        }

        public void Attach()
        {
            _grid.AttachedToVisualTree += OnAttachedToVisualTree;
            _grid.DetachedFromVisualTree += OnDetachedFromVisualTree;
            _grid.Sorting += OnSorting;
            _grid.PropertyChanged += OnGridPropertyChanged;
            EnsureHeaderButtonInstalled();
            UpdateButtonVisibility();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _grid.AttachedToVisualTree -= OnAttachedToVisualTree;
            _grid.DetachedFromVisualTree -= OnDetachedFromVisualTree;
            _grid.Sorting -= OnSorting;
            _grid.PropertyChanged -= OnGridPropertyChanged;
            _button.Click -= OnClearSortClicked;
        }

        private Button BuildButton()
        {
            var icon = new AppIcon
            {
                Kind = "ArrowUpDown",
                Width = 12,
                Height = 12,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
            };

            var btn = new Button
            {
                Classes = { "DataGridSortResetButton" },
                Content = icon,
                IsVisible = false
            };
            ToolTip.SetTip(btn, "清除排序");
            btn.Click += OnClearSortClicked;
            return btn;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            EnsureHeaderButtonInstalled();
            UpdateButtonVisibility();
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            // Keep state; just hide while detached.
            _button.IsVisible = false;
        }

        private void OnGridPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == DataGrid.CollectionViewProperty)
            {
                EnsureHeaderButtonInstalled();
                Dispatcher.UIThread.Post(UpdateButtonVisibility, DispatcherPriority.Background);
            }
        }

        private void OnSorting(object? sender, DataGridColumnEventArgs e)
        {
            EnsureHeaderButtonInstalled();
            Dispatcher.UIThread.Post(UpdateButtonVisibility, DispatcherPriority.Background);
            Dispatcher.UIThread.Post(UpdateButtonVisibility, DispatcherPriority.ContextIdle);
        }

        private void OnClearSortClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var column in _grid.Columns)
                {
                    column.ClearSort();
                }

                var sortDescriptions = _grid.CollectionView?.SortDescriptions;
                sortDescriptions?.Clear();

                UpdateButtonVisibility();
                e.Handled = true;
            }
            catch
            {
                // Keep silent; UI helper should never interrupt page logic.
            }
        }

        private void EnsureHeaderButtonInstalled()
        {
            void TryInstall()
            {
                var topLeft = FindTopLeftHeader();
                if (topLeft is null)
                {
                    return;
                }

                if (!ReferenceEquals(topLeft.Content, _button))
                {
                    topLeft.Content = _button;
                }
            }

            TryInstall();
            Dispatcher.UIThread.Post(TryInstall, DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(TryInstall, DispatcherPriority.ContextIdle);
        }

        private DataGridColumnHeader? FindTopLeftHeader()
        {
            foreach (var c in _grid.GetVisualDescendants())
            {
                if (c is DataGridColumnHeader header && header.Name == "PART_TopLeftCornerHeader")
                {
                    return header;
                }
            }

            return null;
        }

        private void UpdateButtonVisibility()
        {
            var sortDescriptions = _grid.CollectionView?.SortDescriptions;
            var hasSortDescriptions = sortDescriptions is not null && sortDescriptions.Count > 0;
            _button.IsVisible = hasSortDescriptions;
            _button.IsEnabled = _button.IsVisible;
        }
    }
}
