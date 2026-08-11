using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PacToolkits.Desktop.Avalonia.Controls;

public class CardPanel : ContentControl
{
    private bool _hasHeaderIcon;
    private bool _showAccentDot = true;

    public static readonly StyledProperty<object?> TitleProperty =
        AvaloniaProperty.Register<CardPanel, object?>(nameof(Title));

    public static readonly StyledProperty<string?> HeaderHintProperty =
        AvaloniaProperty.Register<CardPanel, string?>(nameof(HeaderHint));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<CardPanel, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<string?> HeaderIconProperty =
        AvaloniaProperty.Register<CardPanel, string?>(nameof(HeaderIcon));

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<CardPanel, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<bool> ShowMenuIconProperty =
        AvaloniaProperty.Register<CardPanel, bool>(nameof(ShowMenuIcon));

    public static readonly StyledProperty<CardPanelChrome> ChromeProperty =
        AvaloniaProperty.Register<CardPanel, CardPanelChrome>(nameof(Chrome), CardPanelChrome.Section);

    public static readonly StyledProperty<bool> IsResponsiveProperty =
        AvaloniaProperty.Register<CardPanel, bool>(nameof(IsResponsive));

    public static readonly StyledProperty<double> ResponsiveCompactThresholdProperty =
        AvaloniaProperty.Register<CardPanel, double>(nameof(ResponsiveCompactThreshold), 680d);

    public static readonly StyledProperty<double> ResponsiveMinimalThresholdProperty =
        AvaloniaProperty.Register<CardPanel, double>(nameof(ResponsiveMinimalThreshold), 400d);

    public static readonly DirectProperty<CardPanel, bool> HasHeaderHintProperty =
        AvaloniaProperty.RegisterDirect<CardPanel, bool>(
            nameof(HasHeaderHint),
            panel => panel.HasHeaderHint);

    public static readonly DirectProperty<CardPanel, bool> ShowDefaultMenuIconProperty =
        AvaloniaProperty.RegisterDirect<CardPanel, bool>(
            nameof(ShowDefaultMenuIcon),
            panel => panel.ShowDefaultMenuIcon);

    public static readonly DirectProperty<CardPanel, bool> HasHeaderIconProperty =
        AvaloniaProperty.RegisterDirect<CardPanel, bool>(
            nameof(HasHeaderIcon),
            panel => panel.HasHeaderIcon);

    public static readonly DirectProperty<CardPanel, bool> ShowAccentDotProperty =
        AvaloniaProperty.RegisterDirect<CardPanel, bool>(
            nameof(ShowAccentDot),
            panel => panel.ShowAccentDot);

    public static readonly DirectProperty<CardPanel, bool> HasTextTitleProperty =
        AvaloniaProperty.RegisterDirect<CardPanel, bool>(
            nameof(HasTextTitle),
            panel => panel.HasTextTitle);

    public static readonly DirectProperty<CardPanel, bool> HasCustomTitleProperty =
        AvaloniaProperty.RegisterDirect<CardPanel, bool>(
            nameof(HasCustomTitle),
            panel => panel.HasCustomTitle);

    static CardPanel()
    {
        TitleProperty.Changed.AddClassHandler<CardPanel>((panel, _) => panel.UpdateTitleSlotState());
        HeaderHintProperty.Changed.AddClassHandler<CardPanel>((panel, _) => panel.UpdateHeaderHintState());
        HeaderIconProperty.Changed.AddClassHandler<CardPanel>((panel, _) => panel.UpdateHeaderMarkerState());
        ShowMenuIconProperty.Changed.AddClassHandler<CardPanel>((panel, _) => panel.UpdateMenuIconState());
        HeaderContentProperty.Changed.AddClassHandler<CardPanel>((panel, _) => panel.UpdateMenuIconState());
        ChromeProperty.Changed.AddClassHandler<CardPanel>((panel, _) => panel.UpdateChromeClasses());
        IsResponsiveProperty.Changed.AddClassHandler<CardPanel>((panel, _) => panel.UpdateResponsiveClasses());
        ResponsiveCompactThresholdProperty.Changed.AddClassHandler<CardPanel>(
            (panel, _) => panel.UpdateResponsiveClasses());
        ResponsiveMinimalThresholdProperty.Changed.AddClassHandler<CardPanel>(
            (panel, _) => panel.UpdateResponsiveClasses());
    }

    public CardPanel()
    {
        UpdateChromeClasses();
        UpdateTitleSlotState();
    }

    public object? Title
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

    public CardPanelChrome Chrome
    {
        get => GetValue(ChromeProperty);
        set => SetValue(ChromeProperty, value);
    }

    public bool IsResponsive
    {
        get => GetValue(IsResponsiveProperty);
        set => SetValue(IsResponsiveProperty, value);
    }

    public double ResponsiveCompactThreshold
    {
        get => GetValue(ResponsiveCompactThresholdProperty);
        set => SetValue(ResponsiveCompactThresholdProperty, value);
    }

    public double ResponsiveMinimalThreshold
    {
        get => GetValue(ResponsiveMinimalThresholdProperty);
        set => SetValue(ResponsiveMinimalThresholdProperty, value);
    }

    public bool HasHeaderHint => !string.IsNullOrWhiteSpace(HeaderHint);

    public bool ShowDefaultMenuIcon => ShowMenuIcon && HeaderContent is null;

    public bool HasHeaderIcon => _hasHeaderIcon;

    public bool ShowAccentDot => _showAccentDot;

    public bool HasTextTitle => Title is string;

    public bool HasCustomTitle => Title is not null && Title is not string;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            UpdateResponsiveClasses();
        }
    }

    private void UpdateHeaderHintState()
        => RaisePropertyChanged(HasHeaderHintProperty, false, HasHeaderHint);

    private void UpdateMenuIconState()
        => RaisePropertyChanged(ShowDefaultMenuIconProperty, false, ShowDefaultMenuIcon);

    private void UpdateTitleSlotState()
    {
        RaisePropertyChanged(HasTextTitleProperty, false, HasTextTitle);
        RaisePropertyChanged(HasCustomTitleProperty, false, HasCustomTitle);
    }

    private void UpdateHeaderMarkerState()
    {
        var hasHeaderIcon = !string.IsNullOrWhiteSpace(HeaderIcon);
        SetAndRaise(HasHeaderIconProperty, ref _hasHeaderIcon, hasHeaderIcon);
        SetAndRaise(ShowAccentDotProperty, ref _showAccentDot, !hasHeaderIcon);
    }

    private void UpdateChromeClasses()
        => Classes.Set("CardChrome", Chrome == CardPanelChrome.Card);

    private void UpdateResponsiveClasses()
    {
        var width = Bounds.Width;
        Classes.Set("ResponsiveEnabled", IsResponsive);
        Classes.Set(
            "ResponsiveCompact",
            IsResponsive && width > 0 && width < ResponsiveCompactThreshold);
        Classes.Set(
            "ResponsiveMinimal",
            IsResponsive && width > 0 && width < ResponsiveMinimalThreshold);
    }
}
