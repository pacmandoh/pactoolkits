using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class StatusPill : UserControl
{
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<StatusPill, string>(nameof(Icon), "Info");

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StatusPill, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty =
        AvaloniaProperty.Register<StatusPill, IBrush?>(nameof(BackgroundBrush));

    public static readonly StyledProperty<IBrush?> PillBorderBrushProperty =
        AvaloniaProperty.Register<StatusPill, IBrush?>(nameof(PillBorderBrush));

    public static readonly StyledProperty<IBrush?> ForegroundBrushProperty =
        AvaloniaProperty.Register<StatusPill, IBrush?>(nameof(ForegroundBrush));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<StatusPill, double>(nameof(IconSize), 14);

    public static readonly StyledProperty<double> TextSizeProperty =
        AvaloniaProperty.Register<StatusPill, double>(nameof(TextSize), 12);

    public static readonly StyledProperty<Thickness> PillPaddingProperty =
        AvaloniaProperty.Register<StatusPill, Thickness>(nameof(PillPadding), new Thickness(10, 4));

    public static readonly StyledProperty<bool> ShowBorderProperty =
        AvaloniaProperty.Register<StatusPill, bool>(nameof(ShowBorder), true);

    public static readonly StyledProperty<object?> SuffixContentProperty =
        AvaloniaProperty.Register<StatusPill, object?>(nameof(SuffixContent));

    public static readonly DirectProperty<StatusPill, bool> HasSuffixContentProperty =
        AvaloniaProperty.RegisterDirect<StatusPill, bool>(nameof(HasSuffixContent), o => o.HasSuffixContent);

    private bool _hasSuffixContent;

    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush? BackgroundBrush
    {
        get => GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    public IBrush? PillBorderBrush
    {
        get => GetValue(PillBorderBrushProperty);
        set => SetValue(PillBorderBrushProperty, value);
    }

    public IBrush? ForegroundBrush
    {
        get => GetValue(ForegroundBrushProperty);
        set => SetValue(ForegroundBrushProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double TextSize
    {
        get => GetValue(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    public Thickness PillPadding
    {
        get => GetValue(PillPaddingProperty);
        set => SetValue(PillPaddingProperty, value);
    }

    public bool ShowBorder
    {
        get => GetValue(ShowBorderProperty);
        set => SetValue(ShowBorderProperty, value);
    }

    public object? SuffixContent
    {
        get => GetValue(SuffixContentProperty);
        set => SetValue(SuffixContentProperty, value);
    }

    public bool HasSuffixContent => _hasSuffixContent;

    public StatusPill()
    {
        InitializeComponent();
        RefreshState();
        UpdateBorderThickness();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SuffixContentProperty)
        {
            RefreshState();
        }
        else if (change.Property == ShowBorderProperty)
        {
            UpdateBorderThickness();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateBorderThickness();
    }

    private void UpdateBorderThickness()
    {
        if (PillChrome is null)
        {
            return;
        }

        PillChrome.BorderThickness = ShowBorder ? new Thickness(1) : new Thickness(0);
    }

    private void RefreshState()
    {
        SetAndRaise(HasSuffixContentProperty, ref _hasSuffixContent, SuffixContent is not null);
    }
}
