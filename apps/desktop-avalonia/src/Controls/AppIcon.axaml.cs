using System;
using Avalonia;
using global::Avalonia.Controls;
using IconPacks.Avalonia.Lucide;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class AppIcon : UserControl
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<AppIcon, string>(nameof(Kind), "Info");

    public static readonly DirectProperty<AppIcon, PackIconLucideKind> ResolvedKindProperty =
        AvaloniaProperty.RegisterDirect<AppIcon, PackIconLucideKind>(nameof(ResolvedKind), o => o.ResolvedKind);

    private PackIconLucideKind _resolvedKind = PackIconLucideKind.Info;

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public PackIconLucideKind ResolvedKind
    {
        get => _resolvedKind;
        private set => SetAndRaise(ResolvedKindProperty, ref _resolvedKind, value);
    }

    public AppIcon()
    {
        InitializeComponent();
        UpdateKind();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KindProperty)
            UpdateKind();
    }

    private void UpdateKind()
    {
        var raw = (Kind ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            ResolvedKind = PackIconLucideKind.Info;
            return;
        }

        if (Enum.TryParse<PackIconLucideKind>(raw, true, out var parsed))
        {
            ResolvedKind = parsed;
            return;
        }

        ResolvedKind = PackIconLucideKind.CircleAlert;
    }
}
