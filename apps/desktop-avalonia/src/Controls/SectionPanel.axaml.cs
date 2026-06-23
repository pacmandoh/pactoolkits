using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PacToolkits.Desktop.Avalonia.Controls;

public class SectionPanel : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SectionPanel, string?>(nameof(Title));

    public static readonly StyledProperty<string?> HeaderHintProperty =
        AvaloniaProperty.Register<SectionPanel, string?>(nameof(HeaderHint));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<SectionPanel, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<SectionPanel, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<bool> ShowMenuIconProperty =
        AvaloniaProperty.Register<SectionPanel, bool>(nameof(ShowMenuIcon));

    public static readonly StyledProperty<string?> FooterTextProperty =
        AvaloniaProperty.Register<SectionPanel, string?>(nameof(FooterText));

    public static readonly DirectProperty<SectionPanel, bool> HasHeaderHintProperty =
        AvaloniaProperty.RegisterDirect<SectionPanel, bool>(
            nameof(HasHeaderHint),
            panel => panel.HasHeaderHint);

    public static readonly DirectProperty<SectionPanel, bool> HasFooterProperty =
        AvaloniaProperty.RegisterDirect<SectionPanel, bool>(
            nameof(HasFooter),
            panel => panel.HasFooter);

    public static readonly DirectProperty<SectionPanel, bool> ShowDefaultMenuIconProperty =
        AvaloniaProperty.RegisterDirect<SectionPanel, bool>(
            nameof(ShowDefaultMenuIcon),
            panel => panel.ShowDefaultMenuIcon);

    static SectionPanel()
    {
        HeaderHintProperty.Changed.AddClassHandler<SectionPanel>((panel, _) => panel.UpdateHeaderHintState());
        FooterTextProperty.Changed.AddClassHandler<SectionPanel>((panel, _) => panel.UpdateFooterState());
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

    public string? FooterText
    {
        get => GetValue(FooterTextProperty);
        set => SetValue(FooterTextProperty, value);
    }

    public bool HasHeaderHint => !string.IsNullOrWhiteSpace(HeaderHint);

    public bool HasFooter => !string.IsNullOrWhiteSpace(FooterText);

    public bool ShowDefaultMenuIcon => ShowMenuIcon && HeaderContent is null;

    private void UpdateHeaderHintState()
        => RaisePropertyChanged(HasHeaderHintProperty, false, HasHeaderHint);

    private void UpdateFooterState()
        => RaisePropertyChanged(HasFooterProperty, false, HasFooter);

    private void UpdateMenuIconState()
        => RaisePropertyChanged(ShowDefaultMenuIconProperty, false, ShowDefaultMenuIcon);
}
