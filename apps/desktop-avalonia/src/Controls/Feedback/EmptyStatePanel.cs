using Avalonia;
using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Controls;

public sealed class EmptyStatePanel : ContentControl
{
    public static readonly StyledProperty<bool> IsEmptyProperty =
        AvaloniaProperty.Register<EmptyStatePanel, bool>(nameof(IsEmpty));

    public static readonly StyledProperty<bool> IsPendingProperty =
        AvaloniaProperty.Register<EmptyStatePanel, bool>(nameof(IsPending));

    public static readonly StyledProperty<double> SlotMinHeightProperty =
        AvaloniaProperty.Register<EmptyStatePanel, double>(nameof(SlotMinHeight));

    public static readonly StyledProperty<SectionSlotMode> SlotModeProperty =
        AvaloniaProperty.Register<EmptyStatePanel, SectionSlotMode>(
            nameof(SlotMode), SectionSlotMode.Always);

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

    public static readonly DirectProperty<EmptyStatePanel, bool> ShowEmptyOverlayProperty =
        AvaloniaProperty.RegisterDirect<EmptyStatePanel, bool>(
            nameof(ShowEmptyOverlay),
            o => o.ShowEmptyOverlay);

    public static readonly DirectProperty<EmptyStatePanel, bool> ShowContentProperty =
        AvaloniaProperty.RegisterDirect<EmptyStatePanel, bool>(
            nameof(ShowContent),
            o => o.ShowContent);

    public static readonly DirectProperty<EmptyStatePanel, double> ContentOpacityProperty =
        AvaloniaProperty.RegisterDirect<EmptyStatePanel, double>(
            nameof(ContentOpacity),
            o => o.ContentOpacity);

    public static readonly DirectProperty<EmptyStatePanel, double> EffectiveSlotMinHeightProperty =
        AvaloniaProperty.RegisterDirect<EmptyStatePanel, double>(
            nameof(EffectiveSlotMinHeight),
            o => o.EffectiveSlotMinHeight);

    public bool IsEmpty { get => GetValue(IsEmptyProperty); set => SetValue(IsEmptyProperty, value); }
    public bool IsPending { get => GetValue(IsPendingProperty); set => SetValue(IsPendingProperty, value); }
    public double SlotMinHeight { get => GetValue(SlotMinHeightProperty); set => SetValue(SlotMinHeightProperty, value); }
    public SectionSlotMode SlotMode { get => GetValue(SlotModeProperty); set => SetValue(SlotModeProperty, value); }
    public string EmptyText { get => GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public string? EmptyHint { get => GetValue(EmptyHintProperty); set => SetValue(EmptyHintProperty, value); }
    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public bool HasHint => !string.IsNullOrWhiteSpace(EmptyHint);

    public bool ShowEmptyOverlay => IsEmpty && !IsPending;

    public bool ShowContent => !IsEmpty && !IsPending;

    public double ContentOpacity => ShowContent ? 1 : 0;

    public double EffectiveSlotMinHeight
        => SlotMode == SectionSlotMode.Always || IsPending ? SlotMinHeight : 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        if (!IsPending)
        {
            return size;
        }

        // pending 时内容仍在树上（opacity 0），不得撑高槽位，否则居中 busy 圈随内容下沉
        var slot = EffectiveSlotMinHeight;
        return slot > 0 ? new Size(size.Width, slot) : size;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EmptyHintProperty)
        {
            RaisePropertyChanged(HasHintProperty, false, HasHint);
        }

        if (change.Property == IsEmptyProperty || change.Property == IsPendingProperty)
        {
            RaisePropertyChanged(ShowEmptyOverlayProperty, false, ShowEmptyOverlay);
            RaisePropertyChanged(ShowContentProperty, false, ShowContent);
            RaisePropertyChanged(ContentOpacityProperty, 0, ContentOpacity);
        }

        if (change.Property == SlotModeProperty
            || change.Property == SlotMinHeightProperty
            || change.Property == IsPendingProperty)
        {
            RaisePropertyChanged(EffectiveSlotMinHeightProperty, 0, EffectiveSlotMinHeight);
            InvalidateMeasure();
        }
    }
}
