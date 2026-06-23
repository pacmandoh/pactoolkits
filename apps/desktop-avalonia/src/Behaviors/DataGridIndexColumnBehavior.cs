using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using PacToolkits.Desktop.Avalonia.Converters;

namespace PacToolkits.Desktop.Avalonia.Behaviors;

public enum DataGridIndexColumnMode
{
    /// <summary>1-based position in the current view (follows sort/filter).</summary>
    DisplayOrder,

    /// <summary><c>DisplayIndex</c> on row item (stable business index).</summary>
    DisplayIndex,

    /// <summary><c>RowNo</c> on row item.</summary>
    RowNo,
}

/// <summary>
/// Seeds a fixed first-column row index (#) once per grid instance.
/// Never subscribes to column collection changes or drops state on visual detach.
/// </summary>
public class DataGridIndexColumnBehavior
{
    public const string IndexColumnTag = "PacToolkits.IndexColumn";

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridIndexColumnBehavior, DataGrid, bool>("IndexColumnEnabled");

    public static readonly AttachedProperty<bool> IsVisibleProperty =
        AvaloniaProperty.RegisterAttached<DataGridIndexColumnBehavior, DataGrid, bool>(
            "IndexColumnVisible", defaultValue: true);

    public static readonly AttachedProperty<DataGridIndexColumnMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<DataGridIndexColumnBehavior, DataGrid, DataGridIndexColumnMode>(
            "IndexColumnMode", DataGridIndexColumnMode.DisplayOrder);

    public static readonly AttachedProperty<string> HeaderProperty =
        AvaloniaProperty.RegisterAttached<DataGridIndexColumnBehavior, DataGrid, string>(
            "IndexColumnHeader", "#");

    private static readonly ConcurrentDictionary<DataGrid, BehaviorState> States = new();
    private static readonly RowIndexConverter RowIndexPlusOne = new();

    static DataGridIndexColumnBehavior()
    {
        EnabledProperty.Changed.AddClassHandler<DataGrid>(OnEnabledChanged);
        IsVisibleProperty.Changed.AddClassHandler<DataGrid>(OnPresentationChanged);
        ModeProperty.Changed.AddClassHandler<DataGrid>(OnPresentationChanged);
        HeaderProperty.Changed.AddClassHandler<DataGrid>(OnPresentationChanged);
    }

    public static bool GetEnabled(DataGrid grid) => grid.GetValue(EnabledProperty);

    public static void SetEnabled(DataGrid grid, bool value) => grid.SetValue(EnabledProperty, value);

    public static bool GetIsVisible(DataGrid grid) => grid.GetValue(IsVisibleProperty);

    public static void SetIsVisible(DataGrid grid, bool value) => grid.SetValue(IsVisibleProperty, value);

    public static DataGridIndexColumnMode GetMode(DataGrid grid) => grid.GetValue(ModeProperty);

    public static void SetMode(DataGrid grid, DataGridIndexColumnMode value) => grid.SetValue(ModeProperty, value);

    public static string GetHeader(DataGrid grid) => grid.GetValue(HeaderProperty);

    public static void SetHeader(DataGrid grid, string value) => grid.SetValue(HeaderProperty, value);

    internal static bool TryGetSortResetHeaderHost(DataGrid grid, out Panel? host)
    {
        host = null;
        return States.TryGetValue(grid, out var state) && state.TryGetHeaderHost(out host);
    }

    internal static bool IsIndexColumn(DataGridColumn? column)
        => column?.Tag as string == IndexColumnTag;

    private static void OnEnabledChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is bool enabled && enabled)
        {
            Attach(grid);
        }
        else
        {
            Detach(grid);
        }
    }

    private static void OnPresentationChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs args)
    {
        if (States.TryGetValue(grid, out var state))
        {
            state.ApplyPresentation();
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
        private DataGridTemplateColumn? _column;
        private bool _disposed;
        private bool _structureSeeded;
        private DataGridIndexColumnMode _appliedMode = (DataGridIndexColumnMode)(-1);
        private string? _appliedHeader;
        private bool? _appliedVisible;
        private Panel? _headerHost;

        public BehaviorState(DataGrid grid) => _grid = grid;

        public bool TryGetHeaderHost(out Panel? host)
        {
            host = _headerHost;
            return host is not null;
        }

        public void Attach()
        {
            TrySeedStructure();
            _grid.Initialized += OnInitialized;
            _grid.AttachedToVisualTree += OnAttachedToVisualTree;

            if (_grid.IsInitialized)
            {
                TrySeedStructure();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _grid.Initialized -= OnInitialized;
            _grid.AttachedToVisualTree -= OnAttachedToVisualTree;
            _column = null;
            _headerHost = null;
        }

        private void OnInitialized(object? sender, EventArgs e)
        {
            _grid.Initialized -= OnInitialized;
            TrySeedStructure();
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
            => TrySeedStructure();

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
                    DataGridSortResetBehavior.NotifyIndexHeaderChanged(_grid);
                    return;
                }

                if (_grid.Columns.Count > 0 && IsIndexColumn(_grid.Columns[0]))
                {
                    _column = (DataGridTemplateColumn)_grid.Columns[0];
                    _structureSeeded = true;
                    ApplyPresentation();
                    DataGridSortResetBehavior.NotifyIndexHeaderChanged(_grid);
                    return;
                }

                _column ??= CreateColumn();
                InsertIndexColumn(_column);
                _structureSeeded = true;
                ApplyPresentation();
                DataGridSortResetBehavior.NotifyIndexHeaderChanged(_grid);
            }
            catch
            {
                // Never interrupt navigation if seed fails; grid remains usable without index column.
            }
        }

        private void InsertIndexColumn(DataGridTemplateColumn column)
        {
            if (_grid.Columns.Contains(column))
            {
                return;
            }

            _grid.Columns.Insert(0, column);
        }

        private bool TryAdoptExistingColumn()
        {
            foreach (var column in _grid.Columns)
            {
                if (IsIndexColumn(column) && column is DataGridTemplateColumn template)
                {
                    _column = template;
                    if (_grid.Columns.IndexOf(column) != 0)
                    {
                        _grid.Columns.Remove(column);
                        _grid.Columns.Insert(0, column);
                    }

                    return true;
                }
            }

            return false;
        }

        public void ApplyPresentation()
        {
            if (_disposed || _column is null)
            {
                return;
            }

            var mode = GetMode(_grid);
            var headerText = GetHeader(_grid);
            var visible = GetIsVisible(_grid);

            if (_appliedVisible != visible)
            {
                _column.IsVisible = visible;
                _appliedVisible = visible;
            }

            if (!string.Equals(_appliedHeader, headerText, StringComparison.Ordinal))
            {
                _column.Header = BuildHeader(headerText);
                _appliedHeader = headerText;
                DataGridSortResetBehavior.NotifyIndexHeaderChanged(_grid);
            }

            if (_appliedMode != mode)
            {
                _column.CellTemplate = BuildCellTemplate(mode);
                _appliedMode = mode;
            }
        }

        private DataGridTemplateColumn CreateColumn()
        {
            var width = 40d;
            if (_grid.TryFindResource("DgWIndex", out var widthValue))
            {
                width = widthValue switch
                {
                    double d => d,
                    DataGridLength dg => dg.Value,
                    _ => width,
                };
            }

            return new DataGridTemplateColumn
            {
                Tag = IndexColumnTag,
                Width = new DataGridLength(width),
                MinWidth = width,
                CanUserSort = false,
                CanUserResize = false,
                IsReadOnly = true,
            };
        }

        private Panel BuildHeader(string headerText)
        {
            var root = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            var label = new TextBlock
            {
                Text = headerText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            root.Children.Add(WrapIndexCell(label));
            _headerHost = root;
            return root;
        }

        private static IDataTemplate BuildCellTemplate(DataGridIndexColumnMode mode)
            => mode switch
            {
                DataGridIndexColumnMode.DisplayIndex => BuildItemFieldTemplate("DisplayIndex"),
                DataGridIndexColumnMode.RowNo => BuildItemFieldTemplate("RowNo"),
                _ => BuildDisplayOrderTemplate(),
            };

        private static FuncDataTemplate<object?> BuildDisplayOrderTemplate()
            => new FuncDataTemplate<object?>((_, _) =>
            {
                var text = CreateIndexTextBlock();
                text.Bind(TextBlock.TextProperty, new Binding(nameof(DataGridRow.Index))
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
                    {
                        AncestorType = typeof(DataGridRow),
                    },
                    Converter = RowIndexPlusOne,
                });
                return WrapIndexCell(text);
            }, supportsRecycling: true);

        private static FuncDataTemplate<object?> BuildItemFieldTemplate(string propertyName)
            => new FuncDataTemplate<object?>((_, _) =>
            {
                var text = CreateIndexTextBlock();
                text.Bind(TextBlock.TextProperty, new ReflectionBinding(propertyName));
                return WrapIndexCell(text);
            }, supportsRecycling: true);

        private static TextBlock CreateIndexTextBlock()
        {
            var text = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            text.Classes.Add("Caption");
            text.Classes.Add("Muted");
            text.Classes.Add("DgIndexCell");
            return text;
        }

        private static Viewbox WrapIndexCell(TextBlock text)
            => new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Child = text,
            };
    }
}
