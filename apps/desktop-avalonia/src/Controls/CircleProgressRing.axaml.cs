using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// 环形进度条：按百分比绘制弧段，支持 indeterminate 与 0→value 回放动画
/// </summary>
public partial class CircleProgressRing : UserControl
{
    // 12 点方向为起点，与旧版环形进度一致
    private const double ProgressOriginAngle = 270;

    private const double IndeterminateSweepAngle = 90;
    private const double PercentToSweepDegrees = 3.6;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CircleProgressRing, double>(nameof(Value));

    public static readonly StyledProperty<double> DisplayValueProperty =
        AvaloniaProperty.Register<CircleProgressRing, double>(nameof(DisplayValue));

    public static readonly StyledProperty<int> ReplayTriggerProperty =
        AvaloniaProperty.Register<CircleProgressRing, int>(nameof(ReplayTrigger));

    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<CircleProgressRing, double>(nameof(Diameter), 52);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<CircleProgressRing, double>(nameof(StrokeThickness), 6);

    public static readonly StyledProperty<IBrush?> ProgressBrushProperty =
        AvaloniaProperty.Register<CircleProgressRing, IBrush?>(nameof(ProgressBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<CircleProgressRing, IBrush?>(nameof(TrackBrush));

    public static readonly StyledProperty<double> ProgressOpacityProperty =
        AvaloniaProperty.Register<CircleProgressRing, double>(nameof(ProgressOpacity), 1);

    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<CircleProgressRing, bool>(nameof(IsIndeterminate));

    public static readonly StyledProperty<bool> IsAnimationEnabledProperty =
        AvaloniaProperty.Register<CircleProgressRing, bool>(nameof(IsAnimationEnabled), true);

    public static readonly StyledProperty<double> ProgressSweepAngleProperty =
        AvaloniaProperty.Register<CircleProgressRing, double>(nameof(ProgressSweepAngle));

    private int _lastReplayTrigger;

    static CircleProgressRing()
    {
        ValueProperty.Changed.AddClassHandler<CircleProgressRing>((ring, e) => ring.OnTargetValueChanged(e));
        ReplayTriggerProperty.Changed.AddClassHandler<CircleProgressRing>((ring, e) => ring.OnReplayTriggerChanged(e));
        IsIndeterminateProperty.Changed.AddClassHandler<CircleProgressRing>((ring, _) => ring.OnIndeterminateChanged());
        IsAnimationEnabledProperty.Changed.AddClassHandler<CircleProgressRing>((ring, _) => ring.ApplyTargetValue());
    }

    public CircleProgressRing()
    {
        InitializeComponent();
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    // 动画中的标签数值（0–100）；弧段走 ProgressSweepAngle
    public double DisplayValue
    {
        get => GetValue(DisplayValueProperty);
        private set => SetValue(DisplayValueProperty, value);
    }

    // 递增以回放 0→value 动画，无需改 Value
    public int ReplayTrigger
    {
        get => GetValue(ReplayTriggerProperty);
        set => SetValue(ReplayTriggerProperty, value);
    }

    public double Diameter
    {
        get => GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public IBrush? ProgressBrush
    {
        get => GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double ProgressOpacity
    {
        get => GetValue(ProgressOpacityProperty);
        set => SetValue(ProgressOpacityProperty, value);
    }

    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    public bool IsAnimationEnabled
    {
        get => GetValue(IsAnimationEnabledProperty);
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    public double ProgressSweepAngle
    {
        get => GetValue(ProgressSweepAngleProperty);
        private set => SetValue(ProgressSweepAngleProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _lastReplayTrigger = ReplayTrigger;

        if (ReplayTrigger > 0)
        {
            ReplayFromZero();
            return;
        }

        if (!IsAnimationEnabled)
        {
            ApplyInstant(ClampPercent(Value));
        }
    }

    private void ReplayFromZero()
    {
        if (IsIndeterminate)
        {
            return;
        }

        var target = ClampPercent(Value);
        if (!IsAnimationEnabled)
        {
            ApplyInstant(target);
            return;
        }

        ApplyInstant(0);
        Dispatcher.UIThread.Post(() => ApplyAnimated(target), DispatcherPriority.Loaded);
    }

    private void OnReplayTriggerChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not int trigger || trigger == _lastReplayTrigger)
        {
            return;
        }

        _lastReplayTrigger = trigger;
        ReplayFromZero();
    }

    private void OnTargetValueChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (IsIndeterminate)
        {
            return;
        }

        ApplyTargetValue();
    }

    private void OnIndeterminateChanged()
    {
        if (IsIndeterminate)
        {
            return;
        }

        ApplyTargetValue();
    }

    private void ApplyTargetValue()
    {
        if (IsIndeterminate)
        {
            return;
        }

        var target = ClampPercent(Value);
        if (!IsAnimationEnabled)
        {
            ApplyInstant(target);
            return;
        }

        ApplyAnimated(target);
    }

    private void ApplyAnimated(double targetPercent)
    {
        DisplayValue = targetPercent;
        ProgressSweepAngle = PercentToDegrees(targetPercent);
    }

    private void ApplyInstant(double targetPercent)
    {
        var degrees = PercentToDegrees(targetPercent);
        SetCurrentValue(DisplayValueProperty, targetPercent);
        SetCurrentValue(ProgressSweepAngleProperty, degrees);
        ProgressArc?.SetCurrentValue(Arc.SweepAngleProperty, degrees);
        ProgressArc?.SetCurrentValue(Arc.StartAngleProperty, ProgressOriginAngle);
    }

    private static double ClampPercent(double value) => Math.Clamp(value, 0, 100);

    private static double PercentToDegrees(double percent) => ClampPercent(percent) * PercentToSweepDegrees;
}
