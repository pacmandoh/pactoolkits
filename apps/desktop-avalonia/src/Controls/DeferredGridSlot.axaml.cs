using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// Placeholder that shows a skeleton until <see cref="MountGrid"/> materializes the grid template.
/// </summary>
public partial class DeferredGridSlot : UserControl
{
    public static readonly StyledProperty<string> SlotNameProperty =
        AvaloniaProperty.Register<DeferredGridSlot, string>(nameof(SlotName), string.Empty);

    public static readonly StyledProperty<IDataTemplate?> GridTemplateProperty =
        AvaloniaProperty.Register<DeferredGridSlot, IDataTemplate?>(nameof(GridTemplate));

    public static readonly StyledProperty<double> SlotMinHeightProperty =
        AvaloniaProperty.Register<DeferredGridSlot, double>(nameof(SlotMinHeight), 120d);

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

    public bool IsMounted
    {
        get => _isMounted;
        private set => SetAndRaise(IsMountedProperty, ref _isMounted, value);
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
