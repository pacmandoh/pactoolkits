using Avalonia;
using Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// 页面数据壳：不可用空态与内容区交互锁
///
/// 页面会换掉 Content，控件上的 IsEnabled 管不到真内容，锁做在模板里
/// </summary>
public sealed class PageDataShell : ContentControl
{
    public static readonly StyledProperty<bool> ShowUnavailableProperty =
        AvaloniaProperty.Register<PageDataShell, bool>(nameof(ShowUnavailable));

    public static readonly StyledProperty<string> UnavailableTitleProperty =
        AvaloniaProperty.Register<PageDataShell, string>(nameof(UnavailableTitle), "暂不可用");

    public static readonly StyledProperty<string?> UnavailableHintProperty =
        AvaloniaProperty.Register<PageDataShell, string?>(nameof(UnavailableHint));

    public static readonly StyledProperty<string> UnavailableIconProperty =
        AvaloniaProperty.Register<PageDataShell, string>(nameof(UnavailableIcon), "Database");

    public static readonly StyledProperty<bool> IsInteractionEnabledProperty =
        AvaloniaProperty.Register<PageDataShell, bool>(nameof(IsInteractionEnabled), true);

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

    public bool IsInteractionEnabled
    {
        get => GetValue(IsInteractionEnabledProperty);
        set => SetValue(IsInteractionEnabledProperty, value);
    }
}
