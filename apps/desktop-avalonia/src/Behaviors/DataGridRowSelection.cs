using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

/// <summary>行选中变更事件参数</summary>
public sealed class DataGridRowSelectionChangedEventArgs : EventArgs
{
    public DataGridRowSelectionChangedEventArgs(int selectedCount, int totalCount)
    {
        SelectedCount = selectedCount;
        TotalCount = totalCount;
    }

    public int SelectedCount { get; }
    public int TotalCount { get; }
}

/// <summary>
/// 植入行 <c>IsSelected</c> 勾选列（Shad BasicDataTable 模式）
/// 与 <see cref="DataGridIndexColumn"/> 共存：插在序号列之后
/// </summary>
public class DataGridRowSelection
{
    public const string SelectionColumnTag = "PacToolkits.SelectionColumn";

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridRowSelection, DataGrid, bool>("RowSelectionEnabled");

    public static readonly AttachedProperty<bool> IsVisibleProperty =
        AvaloniaProperty.RegisterAttached<DataGridRowSelection, DataGrid, bool>(
            "RowSelectionVisible", defaultValue: true);

    public static readonly AttachedProperty<bool?> SelectAllProperty =
        AvaloniaProperty.RegisterAttached<DataGridRowSelection, DataGrid, bool?>("RowSelectionSelectAll");

    public static readonly AttachedProperty<int> SelectedCountProperty =
        AvaloniaProperty.RegisterAttached<DataGridRowSelection, DataGrid, int>("RowSelectionSelectedCount");

    public static readonly AttachedProperty<int> TotalCountProperty =
        AvaloniaProperty.RegisterAttached<DataGridRowSelection, DataGrid, int>("RowSelectionTotalCount");

    private static readonly AttachedProperty<EventHandler<DataGridRowSelectionChangedEventArgs>?> SelectionChangedHandlerProperty =
        AvaloniaProperty.RegisterAttached<DataGridRowSelection, DataGrid, EventHandler<DataGridRowSelectionChangedEventArgs>?>(
            "RowSelectionChanged");

    private static readonly ConcurrentDictionary<DataGrid, BehaviorState> States = new();

    static DataGridRowSelection()
    {
        EnabledProperty.Changed.AddClassHandler<DataGrid>(OnEnabledChanged);
        IsVisibleProperty.Changed.AddClassHandler<DataGrid>(OnPresentationChanged);
        SelectAllProperty.Changed.AddClassHandler<DataGrid>(OnSelectAllChanged);
    }

    public static bool GetEnabled(DataGrid grid) => grid.GetValue(EnabledProperty);

    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);

    public static bool GetIsVisible(DataGrid grid) => grid.GetValue(IsVisibleProperty);

    public static void SetIsVisible(DataGrid grid, bool value) => grid.SetValue(IsVisibleProperty, value);

    public static bool? GetSelectAll(DataGrid grid) => grid.GetValue(SelectAllProperty);

    public static void SetSelectAll(DataGrid grid, bool? value) => grid.SetValue(SelectAllProperty, value);

    public static int GetSelectedCount(DataGrid grid) => grid.GetValue(SelectedCountProperty);

    public static int GetTotalCount(DataGrid grid) => grid.GetValue(TotalCountProperty);

    public static void AddSelectionChangedHandler(
        DataGrid grid,
        EventHandler<DataGridRowSelectionChangedEventArgs> handler)
        => grid.SetValue(SelectionChangedHandlerProperty, handler);

    public static void RemoveSelectionChangedHandler(DataGrid grid)
        => grid.ClearValue(SelectionChangedHandlerProperty);

    internal static bool IsSelectionColumn(DataGridColumn? column)
        => column?.Tag as string == SelectionColumnTag;

    private static void OnEnabledChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is bool enabled && enabled)
        {
            Attach(grid);
        }
        else
        {
            DetachFully(grid);
        }
    }

    private static void OnPresentationChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs args)
    {
        if (States.TryGetValue(grid, out var state))
        {
            state.ApplyPresentation();
        }
    }

    private static void OnSelectAllChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs args)
    {
        if (States.TryGetValue(grid, out var state))
        {
            state.ApplySelectAllFromBinding();
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
        => DetachState(grid, removeColumn: false);

    private static void DetachState(DataGrid grid, bool removeColumn)
    {
        if (!States.TryRemove(grid, out var state))
        {
            return;
        }

        state.Dispose(removeColumn);
    }

    private static void DetachFully(DataGrid grid)
    {
        DetachState(grid, removeColumn: true);
        DataGridVisualLifecycle.Unregister(grid, DetachState);
    }

    private sealed class BehaviorState
    {
        private readonly DataGrid _grid;
        private DataGridTemplateColumn? _column;
        private CheckBox? _headerCheckBox;
        private bool _disposed;
        private bool _structureSeeded;
        private bool? _appliedVisible;
        private bool _syncingSelectAll;
        private bool _syncingHeader;
        private IEnumerable? _itemsSource;
        private readonly HashSet<INotifyPropertyChanged> _rowSubscriptions = new();

        public BehaviorState(DataGrid grid) => _grid = grid;

        public void Attach()
        {
            TrySeedStructure();
            _grid.Initialized += OnInitialized;
            _grid.AttachedToVisualTree += OnAttachedToVisualTree;
            _grid.PropertyChanged += OnGridPropertyChanged;

            if (_grid.IsInitialized)
            {
                TrySeedStructure();
            }

            AttachItemsSource(_grid.ItemsSource);
        }

        public void Dispose(bool removeColumn)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _grid.Initialized -= OnInitialized;
            _grid.AttachedToVisualTree -= OnAttachedToVisualTree;
            _grid.PropertyChanged -= OnGridPropertyChanged;
            DetachItemsSource();
            _headerCheckBox?.IsCheckedChanged -= OnHeaderCheckBoxChanged;

            // visual detach 期间改 Columns 会撞 Avalonia 逻辑树枚举（#13497）；禁用时再移除勾选列
            if (removeColumn && _column is not null && _grid.Columns.Contains(_column))
            {
                _grid.Columns.Remove(_column);
            }

            _column = null;
            _headerCheckBox = null;
            _structureSeeded = false;
        }

        private void OnInitialized(object? sender, EventArgs e)
        {
            _grid.Initialized -= OnInitialized;
            TrySeedStructure();
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
            => TrySeedStructure();

        private void OnGridPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == DataGrid.ItemsSourceProperty)
            {
                DetachItemsSource();
                AttachItemsSource(_grid.ItemsSource);
                UpdateCounts();
            }
        }

        private void TrySeedStructure()
        {
            if (_disposed || _structureSeeded || !GetEnabled(_grid))
            {
                return;
            }

            try
            {
                if (TryAdoptExistingColumn())
                {
                    _structureSeeded = true;
                    ApplyPresentation();
                    UpdateCounts();
                    return;
                }

                _column ??= CreateColumn();
                InsertSelectionColumn(_column);
                _structureSeeded = true;
                ApplyPresentation();
                UpdateCounts();
            }
            catch
            {
                // 无选中列时 grid 仍可用
            }
        }

        private bool TryAdoptExistingColumn()
        {
            foreach (var column in _grid.Columns)
            {
                if (!IsSelectionColumn(column))
                {
                    continue;
                }

                if (column is DataGridCheckBoxColumn)
                {
                    _grid.Columns.Remove(column);
                    return false;
                }

                if (column is not DataGridTemplateColumn templateColumn)
                {
                    continue;
                }

                _column = templateColumn;
                _headerCheckBox = templateColumn.Header as CheckBox;
                DataGridFrozenColumns.SetIsFrozen(_column, true);
                EnsureHeaderCheckBox();
                MoveColumnToSlot(_column);
                return true;
            }

            return false;
        }

        private void InsertSelectionColumn(DataGridTemplateColumn column)
        {
            MoveColumnToSlot(column);
        }

        private void MoveColumnToSlot(DataGridTemplateColumn column)
        {
            if (_grid.Columns.Contains(column))
            {
                _grid.Columns.Remove(column);
            }

            _grid.Columns.Insert(GetInsertIndex(), column);
        }

        private int GetInsertIndex()
            => DataGridInteractionHelper.Rules.SelectionInsertIndex(
                _grid.Columns.Count > 0 && DataGridIndexColumn.IsIndexColumn(_grid.Columns[0]));

        private DataGridTemplateColumn CreateColumn()
        {
            var width = 44d;
            if (_grid.TryFindResource("DgWSelect", out var widthValue))
            {
                width = widthValue switch
                {
                    double d => d,
                    DataGridLength dg => dg.Value,
                    _ => width,
                };
            }

            var header = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsThreeState = true,
            };
            SetHeaderCheckBox(header);

            var column = new DataGridTemplateColumn
            {
                Tag = SelectionColumnTag,
                Header = header,
                CanUserSort = false,
                CanUserResize = false,
                Width = new DataGridLength(width),
                CellTemplate = BuildCellTemplate(),
            };
            DataGridFrozenColumns.SetIsFrozen(column, true);
            return column;
        }

        private static FuncDataTemplate<object?> BuildCellTemplate()
            => new FuncDataTemplate<object?>((_, _) =>
            {
                var checkBox = new CheckBox
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                checkBox.Bind(
                    CheckBox.IsCheckedProperty,
                    new ReflectionBinding(nameof(ISelectableRow.IsSelected))
                    {
                        Mode = BindingMode.TwoWay,
                    });

                return new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Children = { checkBox },
                };
            }, supportsRecycling: true);

        private void EnsureHeaderCheckBox()
        {
            if (_column is null)
            {
                return;
            }

            if (_column.Header is CheckBox existing)
            {
                SetHeaderCheckBox(existing);
                return;
            }

            var header = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsThreeState = true,
            };
            _column.Header = header;
            SetHeaderCheckBox(header);
        }

        private void SetHeaderCheckBox(CheckBox header)
        {
            if (ReferenceEquals(_headerCheckBox, header))
            {
                header.IsCheckedChanged -= OnHeaderCheckBoxChanged;
                header.IsCheckedChanged += OnHeaderCheckBoxChanged;
                return;
            }

            _headerCheckBox?.IsCheckedChanged -= OnHeaderCheckBoxChanged;

            _headerCheckBox = header;
            _headerCheckBox.IsCheckedChanged += OnHeaderCheckBoxChanged;
        }

        public void ApplyPresentation()
        {
            if (_disposed || _column is null)
            {
                return;
            }

            var visible = GetIsVisible(_grid);
            if (_appliedVisible != visible)
            {
                _column.IsVisible = visible;
                _appliedVisible = visible;
                DataGridFrozenColumns.Refresh(_grid);
            }
        }

        public void ApplySelectAllFromBinding()
        {
            // 绑定回写或不确定选择状态下不再更新，避免误清选择和回调重入
            if (_disposed || _syncingSelectAll)
            {
                return;
            }

            var selectAll = GetSelectAll(_grid);
            if (selectAll is null)
            {
                return;
            }

            SetAllRowsSelected(selectAll.Value);
        }

        private void OnHeaderCheckBoxChanged(object? sender, RoutedEventArgs e)
        {
            if (_disposed || _syncingHeader || _headerCheckBox is null)
            {
                return;
            }

            var isChecked = _headerCheckBox.IsChecked;
            // 半选态不回写全选，避免把 indeterminate 当成 false
            if (isChecked is null)
            {
                return;
            }

            SetAllRowsSelected(isChecked.Value);
        }

        private void SetAllRowsSelected(bool selected)
        {
            _syncingSelectAll = true;
            try
            {
                foreach (var row in EnumerateRows())
                {
                    if (row is not INotifyPropertyChanged notify)
                    {
                        continue;
                    }

                    if (notify is not ISelectableRow selectable)
                    {
                        continue;
                    }

                    selectable.IsSelected = selected;
                }
            }
            finally
            {
                _syncingSelectAll = false;
            }

            UpdateCounts();
        }

        private void AttachItemsSource(IEnumerable? source)
        {
            _itemsSource = source;
            if (source is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged += OnItemsCollectionChanged;
            }

            foreach (var row in EnumerateRows())
            {
                SubscribeRow(row);
            }
        }

        private void DetachItemsSource()
        {
            if (_itemsSource is INotifyCollectionChanged collection)
            {
                collection.CollectionChanged -= OnItemsCollectionChanged;
            }

            foreach (var row in _rowSubscriptions.ToArray())
            {
                UnsubscribeRow(row);
            }

            _itemsSource = null;
        }

        private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var row in _rowSubscriptions.ToArray())
                {
                    UnsubscribeRow(row);
                }

                foreach (var row in EnumerateRows())
                {
                    SubscribeRow(row);
                }

                UpdateCounts();
                return;
            }

            if (e.OldItems is not null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is INotifyPropertyChanged row)
                    {
                        UnsubscribeRow(row);
                    }
                }
            }

            if (e.NewItems is not null)
            {
                foreach (var item in e.NewItems)
                {
                    SubscribeRow(item);
                }
            }

            UpdateCounts();
        }

        private void SubscribeRow(object? row)
        {
            if (row is not INotifyPropertyChanged notify || !_rowSubscriptions.Add(notify))
            {
                return;
            }

            notify.PropertyChanged += OnRowPropertyChanged;
        }

        private void UnsubscribeRow(INotifyPropertyChanged row)
        {
            if (!_rowSubscriptions.Remove(row))
            {
                return;
            }

            row.PropertyChanged -= OnRowPropertyChanged;
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(ISelectableRow.IsSelected), StringComparison.Ordinal))
            {
                return;
            }

            if (_syncingSelectAll)
            {
                return;
            }

            UpdateCounts();
        }

        private IEnumerable<object?> EnumerateRows()
        {
            if (_itemsSource is null)
            {
                yield break;
            }

            foreach (var item in _itemsSource)
            {
                yield return item;
            }
        }

        private void UpdateCounts()
        {
            if (_disposed)
            {
                return;
            }

            var rows = EnumerateRows().OfType<ISelectableRow>().ToArray();
            var total = rows.Length;
            var selected = rows.Count(row => row.IsSelected);

            _grid.SetValue(SelectedCountProperty, selected);
            _grid.SetValue(TotalCountProperty, total);

            var selectAll = DataGridInteractionHelper.Rules.SelectAllTriState(selected, total);

            _syncingHeader = true;
            _syncingSelectAll = true;
            try
            {
                _headerCheckBox?.IsChecked = selectAll;

                SetSelectAll(_grid, selectAll);
            }
            finally
            {
                _syncingSelectAll = false;
                _syncingHeader = false;
            }

            if (_grid.GetValue(SelectionChangedHandlerProperty) is { } handler)
            {
                handler(_grid, new DataGridRowSelectionChangedEventArgs(selected, total));
            }
        }
    }
}
