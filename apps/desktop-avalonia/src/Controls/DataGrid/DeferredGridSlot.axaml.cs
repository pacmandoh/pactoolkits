using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using PacToolkits.Desktop.Avalonia.Ui.Interaction;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// 占位骨架：直到 <see cref="MountGrid"/> 物化 grid 模板前显示 skeleton
/// </summary>
public partial class DeferredGridSlot : UserControl
{
    public static readonly StyledProperty<string> SlotNameProperty =
        AvaloniaProperty.Register<DeferredGridSlot, string>(nameof(SlotName), string.Empty);

    public static readonly StyledProperty<IDataTemplate?> GridTemplateProperty =
        AvaloniaProperty.Register<DeferredGridSlot, IDataTemplate?>(nameof(GridTemplate));

    public static readonly StyledProperty<double> SlotMinHeightProperty =
        AvaloniaProperty.Register<DeferredGridSlot, double>(nameof(SlotMinHeight), 120d);

    public static readonly StyledProperty<int> PagerPageIndexProperty =
        AvaloniaProperty.Register<DeferredGridSlot, int>(nameof(PagerPageIndex), 1);

    public static readonly DirectProperty<DeferredGridSlot, bool> IsMountedProperty =
        AvaloniaProperty.RegisterDirect<DeferredGridSlot, bool>(
            nameof(IsMounted),
            slot => slot.IsMounted);

    private bool _isMounted;

    public event EventHandler<DataGrid>? GridMounted;

    public DeferredGridSlot()
    {
        InitializeComponent();
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        MinHeight = 0;
    }

    public string SlotName
    {
        get => GetValue(SlotNameProperty);
        set => SetValue(SlotNameProperty, value);
    }

    public IDataTemplate? GridTemplate
    {
        get => GetValue(GridTemplateProperty);
        set => SetValue(GridTemplateProperty, value);
    }

    public double SlotMinHeight
    {
        get => GetValue(SlotMinHeightProperty);
        set => SetValue(SlotMinHeightProperty, value);
    }

    public int PagerPageIndex
    {
        get => GetValue(PagerPageIndexProperty);
        set => SetValue(PagerPageIndexProperty, value);
    }

    public bool IsMounted
    {
        get => _isMounted;
        private set => SetAndRaise(IsMountedProperty, ref _isMounted, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != PagerPageIndexProperty
            || change.NewValue is not int newIndex
            || change.OldValue is not int oldIndex)
        {
            return;
        }

        if (GridHost.Content is DataGrid grid)
        {
            DataGridInteractionHelper.ClearOnPageChange(grid, oldIndex, newIndex);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (GridHost.Content is Control child)
        {
            child.DataContext = DataContext;
        }
    }

    public void MountGrid()
    {
        if (IsMounted || GridTemplate is null)
        {
            return;
        }

        if (GridTemplate.Build(DataContext) is not DataGrid grid)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(SlotName))
        {
            grid.Name = SlotName;
        }

        grid.HorizontalAlignment = HorizontalAlignment.Stretch;
        grid.VerticalAlignment = VerticalAlignment.Stretch;
        grid.MinHeight = 0;
        grid.DataContext = DataContext;

        GridHost.Content = grid;
        GridHost.IsVisible = true;
        Skeleton.IsVisible = false;
        IsMounted = true;
        GridMounted?.Invoke(this, grid);
    }
}
