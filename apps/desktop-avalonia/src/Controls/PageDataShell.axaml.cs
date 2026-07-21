using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PacToolkits.Desktop.Avalonia.Services.Presentation;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// 页面数据壳：统一 empty / busy / stale / content 切换
/// </summary>
public partial class PageDataShell : UserControl
{
    public static readonly StyledProperty<bool> IsLoadingDataProperty =
        AvaloniaProperty.Register<PageDataShell, bool>(nameof(IsLoadingData));

    public static readonly StyledProperty<bool> ShowUnavailableProperty =
        AvaloniaProperty.Register<PageDataShell, bool>(nameof(ShowUnavailable));

    public static readonly StyledProperty<string> UnavailableTitleProperty =
        AvaloniaProperty.Register<PageDataShell, string>(nameof(UnavailableTitle), "暂不可用");

    public static readonly StyledProperty<string?> UnavailableHintProperty =
        AvaloniaProperty.Register<PageDataShell, string?>(nameof(UnavailableHint));

    public static readonly StyledProperty<string> UnavailableIconProperty =
        AvaloniaProperty.Register<PageDataShell, string>(nameof(UnavailableIcon), "Database");

    public static readonly StyledProperty<bool> ShowStaleProperty =
        AvaloniaProperty.Register<PageDataShell, bool>(nameof(ShowStale));

    public static readonly StyledProperty<string> StaleHintProperty =
        AvaloniaProperty.Register<PageDataShell, string>(
            nameof(StaleHint),
            SectionEmptyCopy.StaleHint);

    public static readonly StyledProperty<bool> ShowLoadingOverlayProperty =
        AvaloniaProperty.Register<PageDataShell, bool>(nameof(ShowLoadingOverlay));

    static PageDataShell()
    {
        ShowUnavailableProperty.Changed.AddClassHandler<PageDataShell>((shell, _) => shell.UpdateLoadingOverlay());
        IsLoadingDataProperty.Changed.AddClassHandler<PageDataShell>((shell, _) => shell.UpdateLoadingOverlay());
    }

    public PageDataShell()
    {
        AvaloniaXamlLoader.Load(this);
        UpdateLoadingOverlay();
    }

    public bool IsLoadingData
    {
        get => GetValue(IsLoadingDataProperty);
        set => SetValue(IsLoadingDataProperty, value);
    }

    public bool ShowUnavailable
    {
        get => GetValue(ShowUnavailableProperty);
        set => SetValue(ShowUnavailableProperty, value);
    }

    public string UnavailableTitle
    {
        get => GetValue(UnavailableTitleProperty);
        set => SetValue(UnavailableTitleProperty, value);
    }

    public string? UnavailableHint
    {
        get => GetValue(UnavailableHintProperty);
        set => SetValue(UnavailableHintProperty, value);
    }

    public string UnavailableIcon
    {
        get => GetValue(UnavailableIconProperty);
        set => SetValue(UnavailableIconProperty, value);
    }

    public bool ShowStale
    {
        get => GetValue(ShowStaleProperty);
        set => SetValue(ShowStaleProperty, value);
    }

    public string StaleHint
    {
        get => GetValue(StaleHintProperty);
        set => SetValue(StaleHintProperty, value);
    }

    public bool ShowLoadingOverlay
    {
        get => GetValue(ShowLoadingOverlayProperty);
        private set => SetValue(ShowLoadingOverlayProperty, value);
    }

    private void UpdateLoadingOverlay()
        => ShowLoadingOverlay = IsLoadingData && !ShowUnavailable;
}
