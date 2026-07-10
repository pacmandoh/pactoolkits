using System;
using Avalonia;
using global::Avalonia.Controls;
using Lucide.Avalonia;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class AppIcon : UserControl
{
    public const double DefaultIconSize = 16;

    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<AppIcon, string>(nameof(Kind), "Info");

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<AppIcon, double>(nameof(StrokeWidth), 2);

    public static readonly DirectProperty<AppIcon, LucideIconKind> ResolvedKindProperty =
        AvaloniaProperty.RegisterDirect<AppIcon, LucideIconKind>(nameof(ResolvedKind), o => o.ResolvedKind);

    public static readonly DirectProperty<AppIcon, double> IconSizeProperty =
        AvaloniaProperty.RegisterDirect<AppIcon, double>(nameof(IconSize), o => o.IconSize);

    private LucideIconKind _resolvedKind = LucideIconKind.Info;
    private double _iconSize = DefaultIconSize;

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public LucideIconKind ResolvedKind
    {
        get => _resolvedKind;
        private set => SetAndRaise(ResolvedKindProperty, ref _resolvedKind, value);
    }

    public double IconSize
    {
        get => _iconSize;
        private set => SetAndRaise(IconSizeProperty, ref _iconSize, value);
    }

    public AppIcon()
    {
        InitializeComponent();
        UpdateKind();
        UpdateIconSize();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateIconSize();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KindProperty)
        {
            UpdateKind();
        }
        else if (change.Property == WidthProperty || change.Property == HeightProperty)
        {
            UpdateIconSize();
        }
    }

    private void UpdateKind()
    {
        var raw = (Kind ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            ResolvedKind = LucideIconKind.Info;
            return;
        }

        if (Enum.TryParse<LucideIconKind>(raw, true, out var parsed))
        {
            ResolvedKind = parsed;
            return;
        }

        ResolvedKind = LucideIconKind.CircleAlert;
    }

    private void UpdateIconSize()
    {
        var width = Width;
        var height = Height;

        if (!double.IsNaN(width) && width > 0)
        {
            IconSize = width;
            return;
        }

        if (!double.IsNaN(height) && height > 0)
        {
            IconSize = height;
            return;
        }

        IconSize = DefaultIconSize;
    }
}
