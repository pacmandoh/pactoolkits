using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using PacToolkits.Desktop.Avalonia.Ui.Theming;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Controls;

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

    public void ZoomIn() => Zoom(0.8);

    public void ZoomOut() => Zoom(1.25);

    private void Zoom(double scale)
    {
        if (_chartRows.Length < 2 || Chart.XAxes.FirstOrDefault() is not Axis axis)
        {
            return;
        }

        ChartZoom.Scale(axis, -0.5, _chartRows.Length - 0.5, scale, 2);
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
        var foreground = ResolveColor("ForegroundColor", Colors.Gray);
        var muted = ResolveColor("MutedColor", Colors.Gray);
        var tooltipBackground = ResolveColor("CardBackgroundColor", Color.FromRgb(30, 30, 30));
        var clients = _chartRows.Select(ClientName).Distinct().ToArray();
        var palette = ResolvePalette();

        Chart.Series = clients.Select((client, index) => (ISeries)new ColumnSeries<double?>
        {
            Name = client,
            Values = _chartRows
                .Select(row => ClientName(row) == client ? ParseValue(row.ValueText) : (double?)null)
                .ToArray(),
            Fill = new SolidColorPaint(ChartColor.ToSkColor(
                palette[index % palette.Length],
                ChartColor.Strong)),
            MaxBarWidth = 28,
            XToolTipLabelFormatter = FormatTooltipTitle,
            YToolTipLabelFormatter = FormatTooltipBody
        }).ToArray();
        Chart.XAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsPaint = new SolidColorPaint(ChartColor.ToSkColor(muted, ChartColor.Text)),
                LabelsRotation = 45,
                TextSize = 11,
                MinStep = 1,
                Padding = new LiveChartsCore.Drawing.Padding(8),
                MinLimit = _chartRows.Length > 10 ? -0.5 : null,
                MaxLimit = _chartRows.Length > 10 ? 9.5 : null
            }
        ];
        Chart.YAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(ChartColor.ToSkColor(foreground, ChartColor.Text)),
                SeparatorsPaint = new SolidColorPaint(ChartColor.ToSkColor(muted, ChartColor.Grid), 1),
                TextSize = 12,
                MinLimit = 0,
                ShowSeparatorLines = true
            }
        ];
        Chart.LegendPosition = LegendPosition.Top;
        Chart.LegendTextPaint = new SolidColorPaint(ChartColor.ToSkColor(foreground, ChartColor.Text));
        Chart.LegendTextSize = 11;
        Chart.TooltipBackgroundPaint = new SolidColorPaint(
            ChartColor.ToSkColor(tooltipBackground, ChartColor.Tooltip));
        Chart.TooltipTextPaint = new SolidColorPaint(
            ChartColor.ToSkColor(foreground, ChartColor.Label));
        Chart.TooltipTextSize = 13;
        Chart.ZoomMode = ZoomAndPanMode.X | ZoomAndPanMode.NoZoomBySection;
        Chart.InvalidateMeasure();
        Chart.InvalidateVisual();
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

        var spec = string.IsNullOrWhiteSpace(row.Sub) ? "-" : row.Sub;
        return $"规格：{spec}\n使用比例：{row.UsagePercentText}\n用量：{row.ValueText}";
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
        => ThemeBrushResolver.TryGetColor(key, out var color) ? color : fallback;

    private Color[] ResolvePalette()
        =>
        [
            ResolveColor("ChartCategoryOrangeColor", Color.FromRgb(255, 122, 26)),
            ResolveColor("ChartCategoryTealColor", Color.FromRgb(20, 184, 166)),
            ResolveColor("ChartCategoryIndigoColor", Color.FromRgb(99, 102, 241)),
            ResolveColor("ChartCategoryYellowColor", Color.FromRgb(251, 191, 36)),
            ResolveColor("ChartCategoryPinkColor", Color.FromRgb(236, 72, 153)),
            ResolveColor("ChartCategoryBlueColor", Color.FromRgb(59, 130, 246)),
            ResolveColor("ChartCategoryPurpleColor", Color.FromRgb(139, 92, 246))
        ];

    private static string ClientName(TrendDrugItem row)
        => string.IsNullOrWhiteSpace(row.ClientDisplay) ? "未知客户端" : row.ClientDisplay;

    private static double ParseValue(string value)
    {
        if (double.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
            || double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return 0;
    }

}
