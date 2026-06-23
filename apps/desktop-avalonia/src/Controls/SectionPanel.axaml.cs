using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PacToolkits.Desktop.Avalonia.Controls;

public class SectionPanel : ContentControl
{
    private bool _hasHeaderIcon;
    private bool _showAccentDot = true;

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SectionPanel, string?>(nameof(Title));

    public static readonly StyledProperty<string?> HeaderHintProperty =
        AvaloniaProperty.Register<SectionPanel, string?>(nameof(HeaderHint));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<SectionPanel, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<string?> HeaderIconProperty =
        AvaloniaProperty.Register<SectionPanel, string?>(nameof(HeaderIcon));

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<SectionPanel, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<bool> ShowMenuIconProperty =
        AvaloniaProperty.Register<SectionPanel, bool>(nameof(ShowMenuIcon));

    public static readonly DirectProperty<SectionPanel, bool> HasHeaderHintProperty =
        AvaloniaProperty.RegisterDirect<SectionPanel, bool>(
            nameof(HasHeaderHint),
            panel => panel.HasHeaderHint);

    public static readonly DirectProperty<SectionPanel, bool> ShowDefaultMenuIconProperty =
        AvaloniaProperty.RegisterDirect<SectionPanel, bool>(
            nameof(ShowDefaultMenuIcon),
            panel => panel.ShowDefaultMenuIcon);

    public static readonly DirectProperty<SectionPanel, bool> HasHeaderIconProperty =
        AvaloniaProperty.RegisterDirect<SectionPanel, bool>(
            nameof(HasHeaderIcon),
            panel => panel.HasHeaderIcon);

    public static readonly DirectProperty<SectionPanel, bool> ShowAccentDotProperty =
        AvaloniaProperty.RegisterDirect<SectionPanel, bool>(
            nameof(ShowAccentDot),
            panel => panel.ShowAccentDot);

    static SectionPanel()
    {
        HeaderHintProperty.Changed.AddClassHandler<SectionPanel>((panel, _) => panel.UpdateHeaderHintState());
        HeaderIconProperty.Changed.AddClassHandler<SectionPanel>((panel, _) => panel.UpdateHeaderMarkerState());
        ShowMenuIconProperty.Changed.AddClassHandler<SectionPanel>((panel, _) => panel.UpdateMenuIconState());
        HeaderContentProperty.Changed.AddClassHandler<SectionPanel>((panel, _) => panel.UpdateMenuIconState());
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? HeaderHint
    {
        get => GetValue(HeaderHintProperty);
        set => SetValue(HeaderHintProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public string? HeaderIcon
    {
        get => GetValue(HeaderIconProperty);
        set => SetValue(HeaderIconProperty, value);
    }

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public bool ShowMenuIcon
    {
        get => GetValue(ShowMenuIconProperty);
        set => SetValue(ShowMenuIconProperty, value);
    }

    public bool HasHeaderHint => !string.IsNullOrWhiteSpace(HeaderHint);

    public bool ShowDefaultMenuIcon => ShowMenuIcon && HeaderContent is null;

    public bool HasHeaderIcon => _hasHeaderIcon;

    public bool ShowAccentDot => _showAccentDot;

    private void UpdateHeaderHintState()
        => RaisePropertyChanged(HasHeaderHintProperty, false, HasHeaderHint);

    private void UpdateMenuIconState()
        => RaisePropertyChanged(ShowDefaultMenuIconProperty, false, ShowDefaultMenuIcon);

    private void UpdateHeaderMarkerState()
    {
        var hasHeaderIcon = !string.IsNullOrWhiteSpace(HeaderIcon);
        SetAndRaise(HasHeaderIconProperty, ref _hasHeaderIcon, hasHeaderIcon);
        SetAndRaise(ShowAccentDotProperty, ref _showAccentDot, !hasHeaderIcon);
    }
}
