using Avalonia;
using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Controls;

public sealed class EmptyStatePanel : ContentControl
{
    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<EmptyStatePanel, bool>(nameof(IsEmpty));

    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<EmptyStatePanel, string>(nameof(EmptyText), "暂无数据");

    public static readonly StyledProperty<string?> EmptyHintProperty =
        AvaloniaProperty.Register<EmptyStatePanel, string?>(nameof(EmptyHint));

    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<EmptyStatePanel, string>(
            nameof(Icon), "CircleAlert");

    public static readonly DirectProperty<EmptyStatePanel, bool> HasHintProperty =
        AvaloniaProperty.RegisterDirect<EmptyStatePanel, bool>(
            nameof(HasHint),
            o => o.HasHint);

    public bool IsEmpty { get => GetValue(IsEmptyProperty); set => SetValue(IsEmptyProperty, value); }
    public string EmptyText { get => GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public string? EmptyHint { get => GetValue(EmptyHintProperty); set => SetValue(EmptyHintProperty, value); }
    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public bool HasHint => !string.IsNullOrWhiteSpace(EmptyHint);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EmptyHintProperty)
        {
            RaisePropertyChanged(HasHintProperty, false, HasHint);
        }
    }
}
