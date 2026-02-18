using Avalonia;
using Avalonia.Controls;
using Material.Icons;

namespace pactoolkits_ui.Controls;

public sealed class EmptyStatePanel : ContentControl
{
    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<EmptyStatePanel, bool>(nameof(IsEmpty));

    public static readonly StyledProperty<string> EmptyTextProperty =
        AvaloniaProperty.Register<EmptyStatePanel, string>(nameof(EmptyText), "暂无数据");

    public static readonly StyledProperty<string?> EmptyHintProperty =
        AvaloniaProperty.Register<EmptyStatePanel, string?>(nameof(EmptyHint));

    public static readonly StyledProperty<MaterialIconKind> IconProperty =
        AvaloniaProperty.Register<EmptyStatePanel, MaterialIconKind>(
            nameof(Icon), MaterialIconKind.AlertCircleOutline);

    public static readonly DirectProperty<EmptyStatePanel, bool> HasHintProperty =
        AvaloniaProperty.RegisterDirect<EmptyStatePanel, bool>(
            nameof(HasHint),
            o => o.HasHint);

    public bool IsEmpty { get => GetValue(IsEmptyProperty); set => SetValue(IsEmptyProperty, value); }
    public string EmptyText { get => GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public string? EmptyHint { get => GetValue(EmptyHintProperty); set => SetValue(EmptyHintProperty, value); }
    public MaterialIconKind Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

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