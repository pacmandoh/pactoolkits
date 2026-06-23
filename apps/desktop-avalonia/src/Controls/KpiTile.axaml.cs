using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace PacToolkits.Desktop.Avalonia.Controls;

public partial class KpiTile : UserControl
{
    private static readonly string[] ToneClasses =
    [
        "ToneDone15",
        "ToneDone25",
        "ToneWarning15",
        "ToneWarning25",
        "ToneDanger15",
        "ToneDanger25",
        "ToneInfo15",
        "ToneInfo25",
        "TonePurple15",
    ];

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

    public KpiTile()
    {
        InitializeComponent();
        Focusable = true;
    }

    static KpiTile()
    {
        PctProperty.Changed.AddClassHandler<KpiTile>((tile, _) => tile.UpdatePctPill());
        PctMetricProperty.Changed.AddClassHandler<KpiTile>((tile, _) => tile.UpdatePctPill());
        ValueForegroundProperty.Changed.AddClassHandler<KpiTile>((tile, _) => tile.UpdateValueForeground());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdatePctPill();
        UpdateValueForeground();
    }

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

    /// <summary>
    /// Which KPI percentage formula drives badge tone/icon:
    /// RemainHealth, UsageIntensity, AbnormalShare, LowStockShare.
    /// </summary>
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

    /// <summary>When unset, <see cref="ValueText"/> uses theme <c>ForegroundColor</c> via styles.</summary>
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

    private void UpdatePctPill()
    {
        if (PctPill is null)
        {
            return;
        }

        var metric = PctMetric ?? PctMetrics.AbnormalShare;
        var tone = ResolveTone(Pct, metric);
        ApplyToneClass(PctPill, tone switch
        {
            KpiPctTone.Done => "ToneDone25",
            KpiPctTone.Warning => "ToneWarning25",
            _ => "ToneDanger25",
        });
        PctPill.Icon = IconForTone(tone, metric);
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
        foreach (var tone in ToneClasses)
        {
            pill.Classes.Remove(tone);
        }

        pill.Classes.Add(toneClass);
    }

    /// <summary>
    /// remain/(remain+used): higher is healthier.
    /// used/(remain+used), abnormal/txn, low-stock share: lower is healthier.
    /// </summary>
    private static KpiPctTone ResolveTone(double pct, string metric)
    {
        if (metric.Equals(PctMetrics.RemainHealth, StringComparison.OrdinalIgnoreCase))
        {
            return pct >= 35 ? KpiPctTone.Done :
                pct >= 15 ? KpiPctTone.Warning :
                KpiPctTone.Danger;
        }

        return pct <= 8 ? KpiPctTone.Done :
            pct <= 25 ? KpiPctTone.Warning :
            KpiPctTone.Danger;
    }

    private static string IconForTone(KpiPctTone tone, string metric)
    {
        if (metric.Equals(PctMetrics.RemainHealth, StringComparison.OrdinalIgnoreCase))
        {
            return tone switch
            {
                KpiPctTone.Done => "Package",
                KpiPctTone.Warning => "PackageOpen",
                _ => "PackageX",
            };
        }

        if (metric.Equals(PctMetrics.UsageIntensity, StringComparison.OrdinalIgnoreCase))
        {
            return tone switch
            {
                KpiPctTone.Done => "CircleDot",
                KpiPctTone.Warning => "ScanBarcode",
                _ => "Flame",
            };
        }

        if (metric.Equals(PctMetrics.AbnormalShare, StringComparison.OrdinalIgnoreCase))
        {
            return tone switch
            {
                KpiPctTone.Done => "ShieldCheck",
                KpiPctTone.Warning => "AlertTriangle",
                _ => "CircleX",
            };
        }

        if (metric.Equals(PctMetrics.LowStockShare, StringComparison.OrdinalIgnoreCase))
        {
            return tone switch
            {
                KpiPctTone.Done => "CircleCheck",
                KpiPctTone.Warning => "AlertTriangle",
                _ => "TriangleAlert",
            };
        }

        return "Percent";
    }

    private enum KpiPctTone
    {
        Done,
        Warning,
        Danger,
    }

    public static class PctMetrics
    {
        public const string RemainHealth = "RemainHealth";
        public const string UsageIntensity = "UsageIntensity";
        public const string AbnormalShare = "AbnormalShare";
        public const string LowStockShare = "LowStockShare";
    }
}
