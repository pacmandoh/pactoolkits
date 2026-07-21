using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class KpiTile : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<KpiTile, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ValueProperty =
        AvaloniaProperty.Register<KpiTile, string?>(nameof(Value));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<KpiTile, string?>(nameof(Hint));

    public static readonly StyledProperty<double> PctProperty =
        AvaloniaProperty.Register<KpiTile, double>(nameof(Pct));

    public static readonly StyledProperty<string?> PctMetricProperty =
        AvaloniaProperty.Register<KpiTile, string?>(nameof(PctMetric));

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<KpiTile, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> ValueForegroundProperty =
        AvaloniaProperty.Register<KpiTile, IBrush?>(nameof(ValueForeground));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<KpiTile, ICommand?>(nameof(Command));

    public static readonly StyledProperty<object?> CommandParameterProperty =
        AvaloniaProperty.Register<KpiTile, object?>(nameof(CommandParameter));

    public static readonly StyledProperty<int> ReplayTriggerProperty =
        AvaloniaProperty.Register<KpiTile, int>(nameof(ReplayTrigger));

    public KpiTile()
    {
        InitializeComponent();
        Focusable = true;
    }

    static KpiTile()
    {
        PctProperty.Changed.AddClassHandler<KpiTile>((tile, _) => tile.UpdatePctTone());
        PctMetricProperty.Changed.AddClassHandler<KpiTile>((tile, _) => tile.UpdatePctTone());
        ValueForegroundProperty.Changed.AddClassHandler<KpiTile>((tile, _) => tile.UpdateValueForeground());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        UpdatePctTone();
        UpdateValueForeground();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => UpdatePctTone();

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public double Pct
    {
        get => GetValue(PctProperty);
        set => SetValue(PctProperty, value);
    }

    // 决定徽章色调/图标与进度环颜色的 KPI 百分比口径：
    // RemainHealth、UsageIntensity、AbnormalShare、LowStockShare
    public string? PctMetric
    {
        get => GetValue(PctMetricProperty);
        set => SetValue(PctMetricProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    // 未设置时 ValueText 经 styles 使用主题 ForegroundColor
    public IBrush? ValueForeground
    {
        get => GetValue(ValueForegroundProperty);
        set => SetValue(ValueForegroundProperty, value);
    }

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public int ReplayTrigger
    {
        get => GetValue(ReplayTriggerProperty);
        set => SetValue(ReplayTriggerProperty, value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Handled || e.InitialPressMouseButton != MouseButton.Left)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > Bounds.Height)
        {
            return;
        }

        ExecuteCommand();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        ExecuteCommand();
        e.Handled = true;
    }

    private void ExecuteCommand()
    {
        var command = Command;
        if (command?.CanExecute(CommandParameter) == true)
        {
            command.Execute(CommandParameter);
        }
    }

    private void UpdatePctTone()
    {
        var metric = PctMetric ?? KpiPctToneHelper.Metrics.AbnormalShare;
        var tone = KpiPctToneHelper.ResolveTone(Pct, metric);

        if (PctPill is { } pill)
        {
            ApplyToneClass(pill, KpiPctToneHelper.ToneClass(tone));
            pill.Icon = KpiPctToneHelper.IconFor(tone, metric);
        }

        PctRing.ProgressBrush = ThemeBrushResolver.GetBrush(
            KpiPctToneHelper.ProgressBrushResourceKey(tone),
            Brushes.Transparent);
    }

    private void UpdateValueForeground()
    {
        if (ValueText is null)
        {
            return;
        }

        if (ValueForeground is { } brush)
        {
            ValueText.Foreground = brush;
            return;
        }

        ValueText.ClearValue(TextBlock.ForegroundProperty);
    }

    private static void ApplyToneClass(StatusPill pill, string toneClass)
    {
        foreach (var tone in KpiPctToneHelper.ToneClasses)
        {
            pill.Classes.Remove(tone);
        }

        pill.Classes.Add(toneClass);
    }

    /// <summary>KPI 百分比口径常量（转发 <c>KpiPctToneHelper.Metrics</c>）</summary>
    public static class PctMetrics
    {
        public const string RemainHealth = KpiPctToneHelper.Metrics.RemainHealth;
        public const string UsageIntensity = KpiPctToneHelper.Metrics.UsageIntensity;
        public const string AbnormalShare = KpiPctToneHelper.Metrics.AbnormalShare;
        public const string LowStockShare = KpiPctToneHelper.Metrics.LowStockShare;
    }
}
