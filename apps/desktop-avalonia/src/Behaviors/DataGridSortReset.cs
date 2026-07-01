using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Collections;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

public class DataGridSortReset
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridSortReset, DataGrid, bool>("Enabled");

    public static readonly AttachedProperty<bool> FilterActiveProperty =
        AvaloniaProperty.RegisterAttached<DataGridSortReset, DataGrid, bool>("FilterActive");

    public static readonly AttachedProperty<ICommand?> ClearFilterCommandProperty =
        AvaloniaProperty.RegisterAttached<DataGridSortReset, DataGrid, ICommand?>("ClearFilterCommand");

    private static readonly ConcurrentDictionary<DataGrid, BehaviorState> States = new();

    static DataGridSortReset()
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

        FilterActiveProperty.Changed.AddClassHandler<DataGrid>((grid, _) =>
        {
            if (States.TryGetValue(grid, out var state))
            {
                state.UpdateHeaderFace();
            }
        });
    }

    public static bool GetEnabled(DataGrid grid) => grid.GetValue(EnabledProperty);

    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);

    public static bool GetFilterActive(DataGrid grid) => grid.GetValue(FilterActiveProperty);

    public static void SetFilterActive(DataGrid grid, bool value) => grid.SetValue(FilterActiveProperty, value);

    public static ICommand? GetClearFilterCommand(DataGrid grid) => grid.GetValue(ClearFilterCommandProperty);

    public static void SetClearFilterCommand(DataGrid grid, ICommand? value)
        => grid.SetValue(ClearFilterCommandProperty, value);

    internal static void RefreshIndexHeader(DataGrid grid)
    {
        if (!States.TryGetValue(grid, out var state))
        {
            return;
        }

        state.RequestHeaderButtonInstall();
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
        private Button? _headerButton;
        private DataGridSortDescriptionCollection? _sortDescriptions;
        private bool _disposed;

        public BehaviorState(DataGrid grid) => _grid = grid;

        public void Attach()
        {
            _grid.AttachedToVisualTree += OnAttachedToVisualTree;
            _grid.Sorting += OnSorting;
            _grid.PropertyChanged += OnGridPropertyChanged;
            _grid.Columns.CollectionChanged += OnColumnsChanged;
            DataGridSortSupport.Apply(_grid);
            AttachSortDescriptions(_grid.CollectionView?.SortDescriptions);
            InstallHeaderButton();
            UpdateHeaderFace();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DetachSortDescriptions();
            _grid.AttachedToVisualTree -= OnAttachedToVisualTree;
            _grid.Sorting -= OnSorting;
            _grid.PropertyChanged -= OnGridPropertyChanged;
            _grid.Columns.CollectionChanged -= OnColumnsChanged;

            _headerButton?.Click -= OnHeaderButtonClicked;
            _headerButton = null;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            DataGridSortSupport.Apply(_grid);
            InstallHeaderButton();
            UpdateHeaderFace();
        }

        private void OnColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            DataGridSortSupport.Apply(_grid);
        }

        private void OnGridPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == DataGrid.CollectionViewProperty)
            {
                DataGridSortSupport.Apply(_grid);
                AttachSortDescriptions(_grid.CollectionView?.SortDescriptions);
                InstallHeaderButton();
                Dispatcher.UIThread.Post(UpdateHeaderFace, DispatcherPriority.Background);
            }
        }

        private void AttachSortDescriptions(DataGridSortDescriptionCollection? sortDescriptions)
        {
            if (ReferenceEquals(_sortDescriptions, sortDescriptions))
            {
                return;
            }

            DetachSortDescriptions();
            _sortDescriptions = sortDescriptions;
            _sortDescriptions?.CollectionChanged += OnSortDescriptionsChanged;
        }

        private void DetachSortDescriptions()
        {
            if (_sortDescriptions is null)
            {
                return;
            }

            _sortDescriptions.CollectionChanged -= OnSortDescriptionsChanged;
            _sortDescriptions = null;
        }

        private void OnSortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.UIThread.Post(UpdateHeaderFace, DispatcherPriority.Background);
        }

        private void OnSorting(object? sender, DataGridColumnEventArgs e)
        {
            InstallHeaderButton();
            Dispatcher.UIThread.Post(UpdateHeaderFace, DispatcherPriority.Background);
        }

        private void OnHeaderButtonClicked(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (GetFilterActive(_grid))
                {
                    var clearFilter = GetClearFilterCommand(_grid);
                    if (clearFilter?.CanExecute(null) == true)
                    {
                        clearFilter.Execute(null);
                    }

                    e.Handled = true;
                    return;
                }

                if (HasActiveSort())
                {
                    foreach (var column in _grid.Columns)
                    {
                        column.ClearSort();
                    }

                    _grid.CollectionView?.SortDescriptions.Clear();
                    UpdateHeaderFace();
                    e.Handled = true;
                }
            }
            catch
            {
                // Keep silent; UI helper should never interrupt page logic.
            }
        }

        public void RequestHeaderButtonInstall() => InstallHeaderButton();

        private void InstallHeaderButton()
        {
            void TryInstall()
            {
                if (!DataGridIndexColumn.TryGetHeaderButton(_grid, out var button) || button is null)
                {
                    return;
                }

                if (!ReferenceEquals(_headerButton, button))
                {
                    _headerButton?.Click -= OnHeaderButtonClicked;

                    _headerButton = button;
                    _headerButton.Click += OnHeaderButtonClicked;
                }
            }

            TryInstall();
            Dispatcher.UIThread.Post(TryInstall, DispatcherPriority.Loaded);
            Dispatcher.UIThread.Post(TryInstall, DispatcherPriority.ContextIdle);
        }

        public void UpdateHeaderFace()
        {
            if (_disposed)
            {
                return;
            }

            if (GetFilterActive(_grid))
            {
                DataGridIndexColumn.SetHeaderFace(_grid, DataGridIndexHeaderFace.ClearFilter);
                return;
            }

            if (HasActiveSort())
            {
                DataGridIndexColumn.SetHeaderFace(_grid, DataGridIndexHeaderFace.ClearSort);
                return;
            }

            DataGridIndexColumn.SetHeaderFace(_grid, DataGridIndexHeaderFace.Default);
        }

        private bool HasActiveSort()
        {
            var sortDescriptions = _grid.CollectionView?.SortDescriptions;
            return sortDescriptions is not null && sortDescriptions.Count > 0;
        }
    }
}
