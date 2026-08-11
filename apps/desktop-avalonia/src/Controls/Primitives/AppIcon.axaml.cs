using System;
using Avalonia;
using Avalonia.Media;
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

    public static readonly DirectProperty<AppIcon, Geometry?> AssetGeometryProperty =
        AvaloniaProperty.RegisterDirect<AppIcon, Geometry?>(nameof(AssetGeometry), o => o.AssetGeometry);

    private LucideIconKind _resolvedKind = LucideIconKind.Info;
    private double _iconSize = DefaultIconSize;
    private Geometry? _assetGeometry;

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

    public Geometry? AssetGeometry
    {
        get => _assetGeometry;
        private set => SetAndRaise(AssetGeometryProperty, ref _assetGeometry, value);
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
            SetLucide(LucideIconKind.Info);
            return;
        }

        // Lucide 优先：避免每个 Kind 都探测 Assets/Icons
        if (Enum.TryParse<LucideIconKind>(raw, true, out var parsed))
        {
            SetLucide(parsed);
            return;
        }

        var geometry = StrokeAssets.TryGet(raw);
        if (geometry is not null)
        {
            AssetGeometry = geometry;
            return;
        }

        SetLucide(LucideIconKind.CircleAlert);
    }

    private void SetLucide(LucideIconKind kind)
    {
        AssetGeometry = null;
        ResolvedKind = kind;
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
