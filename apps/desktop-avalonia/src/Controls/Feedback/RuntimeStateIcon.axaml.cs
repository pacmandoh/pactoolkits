using Avalonia;
using global::Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Contracts.Presentation;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// 使用统一颜色语义呈现活动、过渡和非活动状态
/// </summary>
public partial class RuntimeStateIcon : UserControl
{
    public static readonly StyledProperty<RuntimeVisualState> StateProperty =
        AvaloniaProperty.Register<RuntimeStateIcon, RuntimeVisualState>(nameof(State));

    public static readonly StyledProperty<string> ActiveIconProperty =
        AvaloniaProperty.Register<RuntimeStateIcon, string>(nameof(ActiveIcon), "CirclePlay");

    public static readonly StyledProperty<string> InactiveIconProperty =
        AvaloniaProperty.Register<RuntimeStateIcon, string>(nameof(InactiveIcon), "CircleStop");

    public static readonly DirectProperty<RuntimeStateIcon, bool> IsActiveProperty =
        AvaloniaProperty.RegisterDirect<RuntimeStateIcon, bool>(nameof(IsActive), control => control.IsActive);

    public static readonly DirectProperty<RuntimeStateIcon, bool> IsTransitioningProperty =
        AvaloniaProperty.RegisterDirect<RuntimeStateIcon, bool>(nameof(IsTransitioning), control => control.IsTransitioning);

    public static readonly DirectProperty<RuntimeStateIcon, bool> IsInactiveProperty =
        AvaloniaProperty.RegisterDirect<RuntimeStateIcon, bool>(nameof(IsInactive), control => control.IsInactive);

    private bool _isActive;
    private bool _isTransitioning;
    private bool _isInactive = true;

    public RuntimeVisualState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string ActiveIcon
    {
        get => GetValue(ActiveIconProperty);
        set => SetValue(ActiveIconProperty, value);
    }

    public string InactiveIcon
    {
        get => GetValue(InactiveIconProperty);
        set => SetValue(InactiveIconProperty, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set => SetAndRaise(IsActiveProperty, ref _isActive, value);
    }

    public bool IsTransitioning
    {
        get => _isTransitioning;
        private set => SetAndRaise(IsTransitioningProperty, ref _isTransitioning, value);
    }

    public bool IsInactive
    {
        get => _isInactive;
        private set => SetAndRaise(IsInactiveProperty, ref _isInactive, value);
    }

    public RuntimeStateIcon()
    {
        InitializeComponent();
        ApplyState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StateProperty)
        {
            ApplyState();
        }
    }

    private void ApplyState()
    {
        IsActive = State == RuntimeVisualState.Active;
        IsTransitioning = State == RuntimeVisualState.Transitioning;
        IsInactive = State == RuntimeVisualState.Inactive;
    }
}
