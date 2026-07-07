using System;
using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using PacToolkits.Desktop.Avalonia.Controls;
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

public enum DataGridIndexHeaderFace
{
    Default,
    ClearSort,
    ClearFilter,
}

/// <summary>
/// Seeds a fixed first-column row index (#) once per grid instance.
/// Does not subscribe to column collection changes.
/// </summary>
public class DataGridIndexColumn
{
    public const string IndexColumnTag = "PacToolkits.IndexColumn";

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<DataGridIndexColumn, DataGrid, bool>("IndexColumnEnabled");

    public static readonly AttachedProperty<bool> IsVisibleProperty =
        AvaloniaProperty.RegisterAttached<DataGridIndexColumn, DataGrid, bool>(
            "IndexColumnVisible", defaultValue: true);

    public static readonly AttachedProperty<DataGridIndexColumnMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<DataGridIndexColumn, DataGrid, DataGridIndexColumnMode>(
            "IndexColumnMode", DataGridIndexColumnMode.DisplayOrder);

    public static readonly AttachedProperty<string> HeaderProperty =
        AvaloniaProperty.RegisterAttached<DataGridIndexColumn, DataGrid, string>(
            "IndexColumnHeader", "#");

    private static readonly ConcurrentDictionary<DataGrid, BehaviorState> States = new();
    private static readonly RowIndexConverter RowIndexPlusOne = new();

    static DataGridIndexColumn()
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

    internal static bool TryGetHeaderButton(DataGrid grid, out Button? button)
    {
        button = null;
        return States.TryGetValue(grid, out var state) && state.TryGetHeaderButton(out button);
    }

    internal static void SetHeaderFace(DataGrid grid, DataGridIndexHeaderFace face)
    {
        if (States.TryGetValue(grid, out var state))
        {
            state.ApplyHeaderFace(face);
        }
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
        if (!States.TryRemove(grid, out var state))
        {
            return;
        }

        state.Dispose();
    }

    private static void DetachFully(DataGrid grid)
    {
        DetachState(grid);
        DataGridVisualLifecycle.Unregister(grid);
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
        private Button? _headerButton;
        private TextBlock? _headerLabel;
        private AppIcon? _headerIcon;
        private DataGridIndexHeaderFace _headerFace = DataGridIndexHeaderFace.Default;

        public BehaviorState(DataGrid grid) => _grid = grid;

        public bool TryGetHeaderButton(out Button? button)
        {
            button = _headerButton;
            return button is not null;
        }

        public void ApplyHeaderFace(DataGridIndexHeaderFace face)
        {
            if (_disposed || _headerButton is null || _headerLabel is null || _headerIcon is null)
            {
                return;
            }

            _headerFace = face;
            switch (face)
            {
                case DataGridIndexHeaderFace.ClearSort:
                    _headerLabel.IsVisible = false;
                    _headerIcon.Kind = "ArrowUpDown";
                    _headerIcon.IsVisible = true;
                    _headerButton.IsEnabled = true;
                    break;
                case DataGridIndexHeaderFace.ClearFilter:
                    _headerLabel.IsVisible = false;
                    _headerIcon.Kind = "SearchX";
                    _headerIcon.IsVisible = true;
                    _headerButton.IsEnabled = true;
                    break;
                default:
                    _headerLabel.IsVisible = true;
                    _headerIcon.IsVisible = false;
                    _headerButton.IsEnabled = false;
                    break;
            }
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
            _headerButton = null;
            _headerLabel = null;
            _headerIcon = null;
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
                    DataGridSortReset.RefreshIndexHeader(_grid);
                    return;
                }

                if (_grid.Columns.Count > 0 && IsIndexColumn(_grid.Columns[0]))
                {
                    _column = (DataGridTemplateColumn)_grid.Columns[0];
                    _structureSeeded = true;
                    ApplyPresentation();
                    DataGridSortReset.RefreshIndexHeader(_grid);
                    return;
                }

                _column ??= CreateColumn();
                InsertIndexColumn(_column);
                _structureSeeded = true;
                ApplyPresentation();
                DataGridSortReset.RefreshIndexHeader(_grid);
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
                DataGridSortReset.RefreshIndexHeader(_grid);
            }

            if (_appliedMode != mode)
            {
                _column.CellTemplate = BuildCellTemplate(mode);
                _appliedMode = mode;
            }

            ApplyHeaderFace(_headerFace);
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
            _headerLabel = new TextBlock
            {
                Text = headerText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _headerIcon = new AppIcon
            {
                IsVisible = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var face = new Grid();
            face.Children.Add(WrapIndexCell(_headerLabel));
            face.Children.Add(_headerIcon);

            _headerButton = new Button
            {
                Classes = { "DgIndexHeaderButton", "Ghost", "Icon" },
                Content = face,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0),
                IsEnabled = false,
            };

            var root = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            root.Children.Add(_headerButton);
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
