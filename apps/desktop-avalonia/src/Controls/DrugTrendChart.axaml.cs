using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using SkiaSharp;

namespace PacToolkits.Desktop.Avalonia.Controls;

/// <summary>
/// Dashboard-style column chart for the same rows shown by the drug trend grid.
/// The chart is kept behind a deferred host so LiveCharts/Skia construction never
/// participates in the normal dashboard navigation path.
/// </summary>
public partial class DrugTrendChart : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<DrugTrendChart, IEnumerable?>(nameof(ItemsSource));

    private INotifyCollectionChanged? _observableItems;
    private bool _rebuildQueued;

    static DrugTrendChart()
    {
        ItemsSourceProperty.Changed.AddClassHandler<DrugTrendChart>((chart, _) => chart.OnItemsSourceChanged());
    }

    public DrugTrendChart()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => QueueRebuild();
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeItems();
        RebuildChart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeItems();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnItemsSourceChanged()
    {
        UnsubscribeItems();
        SubscribeItems();
        QueueRebuild();
    }

    private void SubscribeItems()
    {
        if (_observableItems is not null)
        {
            return;
        }

        _observableItems = ItemsSource as INotifyCollectionChanged;
        _observableItems?.CollectionChanged += OnCollectionChanged;
    }

    private void UnsubscribeItems()
    {
        _observableItems?.CollectionChanged -= OnCollectionChanged;
        _observableItems = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueRebuild();

    private void QueueRebuild()
    {
        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            RebuildChart();
        }, DispatcherPriority.Background);
    }

    private TrendDrugItem[] _chartRows = [];

    private void RebuildChart()
    {
        if (Chart is null)
        {
            return;
        }

        _chartRows = ItemsSource?.OfType<TrendDrugItem>().ToArray() ?? [];
        var labels = _chartRows.Select(row => row.Name).ToArray();
        var values = _chartRows.Select(row => ParseValue(row.ValueText)).ToArray();
        var primary = ResolveColor("PrimaryColor", Color.FromRgb(57, 168, 255));
        var foreground = ResolveColor("ForegroundColor", Colors.Gray);
        var muted = ResolveColor("MutedColor", Colors.Gray);
        var primaryForeground = ResolveColor("PrimaryForegroundColor", Colors.White);

        Chart.Series =
        [
            new ColumnSeries<double>
            {
                Name = string.Empty,
                Values = values,
                Fill = new SolidColorPaint(ToSkColor(primary)),
                XToolTipLabelFormatter = FormatTooltipTitle,
                YToolTipLabelFormatter = FormatTooltipBody
            }
        ];
        Chart.XAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsPaint = new SolidColorPaint(ToSkColor(muted)),
                LabelsRotation = 45,
                TextSize = 11,
                MinStep = 1,
                Padding = new LiveChartsCore.Drawing.Padding(8)
            }
        ];
        Chart.YAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(ToSkColor(foreground)),
                TextSize = 12,
                MinLimit = 0,
                ShowSeparatorLines = false
            }
        ];
        Chart.TooltipBackgroundPaint = new SolidColorPaint(ToSkColor(primary));
        Chart.TooltipTextPaint = new SolidColorPaint(ToSkColor(primaryForeground));
        Chart.TooltipTextSize = 14;
    }

    private string FormatTooltipTitle(LiveChartsCore.Kernel.ChartPoint point)
    {
        var row = ResolveRow(point);
        return row is null ? string.Empty : $"● {row.Name}";
    }

    private string FormatTooltipBody(LiveChartsCore.Kernel.ChartPoint point)
    {
        var row = ResolveRow(point);
        if (row is null)
        {
            return string.Empty;
        }

        var spec = row.SpecDisplay;
        if (string.IsNullOrWhiteSpace(spec))
        {
            return $"用量：{row.ValueText}";
        }

        return $"规格：{spec}\n用量：{row.ValueText}";
    }

    private TrendDrugItem? ResolveRow(LiveChartsCore.Kernel.ChartPoint point)
    {
        var index = point.Index;
        if (index < 0 || index >= _chartRows.Length)
        {
            return null;
        }

        return _chartRows[index];
    }

    private Color ResolveColor(string key, Color fallback)
    {
        var app = global::Avalonia.Application.Current;
        if (app?.TryFindResource(key, ActualThemeVariant, out var value) == true)
        {
            return value switch
            {
                Color color => color,
                ISolidColorBrush brush => brush.Color,
                _ => fallback
            };
        }

        if (ActualThemeVariant != ThemeVariant.Default
            && app?.TryFindResource(key, ThemeVariant.Default, out value) == true)
        {
            return value switch
            {
                Color color => color,
                ISolidColorBrush brush => brush.Color,
                _ => fallback
            };
        }

        return fallback;
    }

    private static double ParseValue(string value)
    {
        if (double.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
            || double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static SKColor ToSkColor(Color color)
        => new(color.R, color.G, color.B, color.A);
}
