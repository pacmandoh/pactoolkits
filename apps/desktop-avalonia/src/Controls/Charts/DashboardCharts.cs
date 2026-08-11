using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Events;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Ui.Theming;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using SkiaSharp;

namespace PacToolkits.Desktop.Avalonia.Controls;

public sealed class TxnChart : DashboardChart<TxnItem, CartesianChart>
{
    private TxnItem[] _rows = [];

    public TxnChart(IEnumerable<TxnItem> items)
        : base(items, new CartesianChart
        {
            MinHeight = 320,
            Margin = new Thickness(12, 8, 12, 12),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch
        })
    {
    }

    public void ZoomIn() => Zoom(0.8);

    public void ZoomOut() => Zoom(1.25);

    private void Zoom(double scale)
    {
        if (_rows.Length < 2 || Chart.XAxes.FirstOrDefault() is not Axis axis)
        {
            return;
        }

        ChartZoom.Scale(
            axis,
            _rows[0].CreatedAt.LocalDateTime.Ticks,
            _rows[^1].CreatedAt.LocalDateTime.Ticks,
            scale,
            TimeSpan.FromMinutes(1).Ticks);
    }

    protected override void RebuildChart()
    {
        _rows = Items.OrderBy(row => row.CreatedAt).ToArray();
        var foreground = ResolveColor("ForegroundColor", Colors.Gray);
        var muted = ResolveColor("MutedColor", Colors.Gray);
        var background = ResolveColor("BackgroundColor", Colors.Black);
        var crosshair = ResolveColor("ChartCategoryOrangeColor", Color.FromRgb(255, 122, 26));
        var crosshairLabel = ResolveColor("ForegroundColor", Colors.White);
        var badges = _rows.Select(row => row.Badge).Distinct().OrderBy(BadgeOrder).ToArray();
        var visibleRows = _rows.TakeLast(10).ToArray();
        var visibleMin = visibleRows.FirstOrDefault()?.CreatedAt.LocalDateTime.Ticks;
        var visibleMax = visibleRows.LastOrDefault()?.CreatedAt.LocalDateTime.Ticks;
        if (visibleMin == visibleMax && visibleMin is not null)
        {
            visibleMin -= TimeSpan.FromMinutes(1).Ticks;
            visibleMax += TimeSpan.FromMinutes(1).Ticks;
        }

        Chart.Series = badges.Select(badge => (ISeries)new LineSeries<DateTimePoint>
        {
            Name = BadgeName(badge),
            Values = _rows
                .Select(row => new DateTimePoint(
                    row.CreatedAt.LocalDateTime,
                    row.Badge == badge ? ParseNumber(row.Qty) : null))
                .ToArray(),
            Stroke = new SolidColorPaint(ToSkColor(BadgeColor(badge), ChartColor.Strong), 2),
            Fill = new LinearGradientPaint(
                ToSkColor(BadgeColor(badge), ChartColor.AreaTop),
                ToSkColor(BadgeColor(badge), ChartColor.AreaBottom),
                new SKPoint(0, 0),
                new SKPoint(0, 1)),
            GeometryFill = new SolidColorPaint(ToSkColor(BadgeColor(badge), ChartColor.Solid)),
            GeometryStroke = new SolidColorPaint(ToSkColor(background, ChartColor.Muted), 2),
            GeometrySize = 8,
            LineSmoothness = 0.3,
            EnableNullSplitting = false,
            XToolTipLabelFormatter = FormatTooltipTitle,
            YToolTipLabelFormatter = FormatTooltipBody
        }).ToArray();
        Chart.XAxes =
        [
            new Axis
            {
                Labels = [],
                LabelsPaint = null,
                TextSize = 0,
                MinStep = TimeSpan.FromMinutes(1).Ticks,
                UnitWidth = TimeSpan.FromMinutes(1).Ticks,
                Padding = new LiveChartsCore.Drawing.Padding(0),
                ShowSeparatorLines = false,
                Labeler = value => new DateTime((long)value).ToString("MM-dd HH:mm", CultureInfo.CurrentCulture),
                CrosshairPaint = new SolidColorPaint(ToSkColor(crosshair, ChartColor.Crosshair), 1),
                CrosshairLabelsPaint = new SolidColorPaint(ToSkColor(crosshairLabel, ChartColor.Label)),
                CrosshairLabelsBackground = new LiveChartsCore.Drawing.LvcColor(
                    crosshair.R, crosshair.G, crosshair.B, ChartColor.Tooltip),
                CrosshairPadding = new LiveChartsCore.Drawing.Padding(6),
                CrosshairSnapEnabled = true,
                MinLimit = _rows.Length > 10 ? visibleMin : null,
                MaxLimit = _rows.Length > 10 ? visibleMax : null
            }
        ];
        Chart.YAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(ToSkColor(foreground, ChartColor.Text)),
                SeparatorsPaint = new SolidColorPaint(ToSkColor(muted, ChartColor.Grid), 1),
                TextSize = 11,
                MinLimit = 0,
                ShowSeparatorLines = true,
                CrosshairPaint = new SolidColorPaint(ToSkColor(crosshair, ChartColor.Crosshair), 1),
                CrosshairLabelsPaint = new SolidColorPaint(ToSkColor(crosshairLabel, ChartColor.Label)),
                CrosshairLabelsBackground = new LiveChartsCore.Drawing.LvcColor(
                    crosshair.R, crosshair.G, crosshair.B, ChartColor.Tooltip),
                CrosshairPadding = new LiveChartsCore.Drawing.Padding(6),
                CrosshairSnapEnabled = true
            }
        ];
        Chart.LegendPosition = LegendPosition.Top;
        Chart.LegendTextPaint = new SolidColorPaint(ToSkColor(foreground, ChartColor.Text));
        Chart.LegendTextSize = 11;
        Chart.ZoomMode = ZoomAndPanMode.X | ZoomAndPanMode.NoZoomBySection;
        ApplyTooltip();
    }

    private string FormatTooltipTitle(LiveChartsCore.Kernel.ChartPoint point)
        => ResolveRow(point) is { } row ? $"● {row.DrugId}" : string.Empty;

    private string FormatTooltipBody(LiveChartsCore.Kernel.ChartPoint point)
    {
        var row = ResolveRow(point);
        return row is null
            ? string.Empty
            : $"规格：{(string.IsNullOrWhiteSpace(row.Spec) ? "-" : row.Spec)}\n时间：{row.Time}\n用量：{row.Qty}";
    }

    private TxnItem? ResolveRow(LiveChartsCore.Kernel.ChartPoint point)
    {
        var index = point.Index;
        return index < 0 || index >= _rows.Length ? null : _rows[index];
    }

    private Color BadgeColor(TxnBadge badge)
        => badge switch
        {
            TxnBadge.Done => ResolveColor("ChartStatusDoneColor", Color.FromRgb(163, 230, 53)),
            TxnBadge.Warning => ResolveColor("ChartStatusWarningColor", Color.FromRgb(251, 146, 60)),
            TxnBadge.Danger => ResolveColor("ChartStatusDangerColor", Color.FromRgb(244, 63, 94)),
            _ => ResolveColor("ChartStatusMutedColor", Color.FromRgb(148, 163, 184))
        };

    private static string BadgeName(TxnBadge badge)
        => badge switch
        {
            TxnBadge.Done => "完成",
            TxnBadge.Warning => "警告",
            TxnBadge.Danger => "异常",
            _ => "未知"
        };

    private static int BadgeOrder(TxnBadge badge)
        => badge switch
        {
            TxnBadge.Done => 0,
            TxnBadge.Warning => 1,
            TxnBadge.Danger => 2,
            _ => 3
        };
}

public sealed class ClientChart : DashboardChart<TopClientItem, PieChart>
{
    private string? _selectedClient;

    public ClientChart(IEnumerable<TopClientItem> items, string? selectedClient)
        : base(items, new PieChart
        {
            MinHeight = 200,
            Margin = new Thickness(12, 8, 12, 12),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
            LegendPosition = LegendPosition.Right,
            LegendTextSize = 11
        })
    {
        _selectedClient = NormalizeClient(selectedClient);
    }

    public void SetSelectedClient(string? selectedClient)
    {
        var normalized = NormalizeClient(selectedClient);
        if (_selectedClient == normalized)
        {
            return;
        }

        _selectedClient = normalized;
        QueueRebuild();
    }

    protected override void RebuildChart()
    {
        var slices = BuildSlices();
        var total = slices.Sum(slice => slice.Value);
        var palette = ResolveClientPalette();

        var hasSelection = _selectedClient is not null;
        var irrelevant = ResolveColor("ChartCategoryIrrelevantColor", Color.FromRgb(100, 116, 139));

        Chart.Series = slices.Select(slice => (ISeries)new PieSeries<double>
        {
            Name = slice.Name,
            Values = [slice.Value],
            Fill = new SolidColorPaint(ToSkColor(
                hasSelection && !IsSelected(slice.ClientRaws)
                    ? irrelevant
                    : palette[slice.ColorIndex % palette.Length],
                ChartColor.Shape)),
            InnerRadius = 54,
            Pushout = IsSelected(slice.ClientRaws) ? 10 : 0,
            HoverPushout = IsSelected(slice.ClientRaws) ? 10 : 4,
            CornerRadius = 5,
            MaxRadialColumnWidth = 42,
            ToolTipLabelFormatter = _ => $"{slice.Value:N0}（{FormatPercent(slice.Value, total)}）"
        }).ToArray();

        Chart.LegendTextPaint = new SolidColorPaint(ToSkColor(
            ResolveColor("ForegroundColor", Colors.Gray),
            ChartColor.Text));
        ApplyTooltip();
    }

    private ClientSlice[] BuildSlices()
    {
        var rows = Items
            .Select((row, index) => new ClientSlice(
                row.Machines.ToHashSet(StringComparer.OrdinalIgnoreCase),
                row.ClientDisplay,
                ParseNumber(row.Value),
                index))
            .Where(slice => slice.Value > 0)
            .ToArray();
        if (rows.Length <= 7)
        {
            return rows;
        }

        ClientSlice? selected = _selectedClient is null
            ? null
            : rows.FirstOrDefault(row => IsSelected(row.ClientRaws));
        var visible = selected is null || rows.Take(6).Any(row => IsSelected(row.ClientRaws))
            ? rows.Take(6).ToArray()
            : rows.Take(5).Append(selected.Value).ToArray();
        var visibleKeys = visible.Select(row => row.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var otherValue = rows.Where(row => !visibleKeys.Contains(row.Name)).Sum(row => row.Value);

        return visible
            .Append(new ClientSlice(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "其他",
                otherValue,
                6))
            .ToArray();
    }

    private Color[] ResolveClientPalette()
        =>
        [
            ResolveColor("ChartCategoryPurpleColor", Color.FromRgb(139, 92, 246)),
            ResolveColor("ChartCategoryOrangeColor", Color.FromRgb(255, 122, 26)),
            ResolveColor("ChartCategoryTealColor", Color.FromRgb(20, 184, 166)),
            ResolveColor("ChartCategoryPinkColor", Color.FromRgb(236, 72, 153)),
            ResolveColor("ChartCategoryBlueColor", Color.FromRgb(59, 130, 246)),
            ResolveColor("ChartCategoryYellowColor", Color.FromRgb(251, 191, 36)),
            ResolveColor("ChartCategoryIndigoColor", Color.FromRgb(99, 102, 241))
        ];

    private bool IsSelected(IReadOnlySet<string> clientRaws)
        => _selectedClient is not null
           && clientRaws.Any(raw => ClientEquals(raw, _selectedClient));

    private readonly record struct ClientSlice(
        IReadOnlySet<string> ClientRaws,
        string Name,
        double Value,
        int ColorIndex);
}

public sealed class EntryChart : DashboardChart<EntryChartItem, CartesianChart>
{
    private string? _selectedClient;

    public EntryChart(IEnumerable<EntryChartItem> items, string? selectedClient)
        : base(items, new CartesianChart
        {
            MinHeight = 200,
            Margin = new Thickness(12, 8, 12, 12),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
            LegendPosition = LegendPosition.Top,
            LegendTextSize = 11,
            FindingStrategy = FindingStrategy.CompareOnlyY,
        })
    {
        _selectedClient = NormalizeClient(selectedClient);
    }

    public void SetSelectedClient(string? selectedClient)
    {
        var normalized = NormalizeClient(selectedClient);
        if (_selectedClient == normalized)
        {
            return;
        }

        _selectedClient = normalized;
        QueueRebuild();
    }

    protected override void RebuildChart()
    {
        var rows = BuildRows();
        TraceEntryState[] states =
        [
            TraceEntryState.Success,
            TraceEntryState.Warning,
            TraceEntryState.Failed
        ];
        var irrelevant = ResolveColor("ChartCategoryIrrelevantColor", Color.FromRgb(100, 116, 139));
        var irrelevantPaint = new SolidColorPaint(ToSkColor(irrelevant, ChartColor.Shape));
        var hasSelection = _selectedClient is not null;
        Chart.Series = states.Select(state =>
        {
            var stateName = StateName(state);
            var stateColor = StateColor(state);
            var series = new StackedRowSeries<EntryPoint?>
            {
                Name = stateName,
                Values = rows.Select(row =>
                {
                    var value = row.Counts.GetValueOrDefault(state);
                    return value <= 0
                        ? null
                        : new EntryPoint(
                            value,
                            hasSelection && !row.ClientRaws.Contains(_selectedClient!),
                            row.ClientDisplay);
                }).ToArray(),
                Mapping = (point, index) => point is null
                    ? Coordinate.Empty
                    : new Coordinate(index, point.Value),
                Fill = new SolidColorPaint(ToSkColor(stateColor, ChartColor.Shape)),
                MaxBarWidth = 28,
                XToolTipLabelFormatter = point => point.Context.DataSource is EntryPoint item
                    ? $"● {item.ClientDisplay}"
                    : string.Empty,
                YToolTipLabelFormatter = point => $"{point.Model?.Value ?? 0:N0} 条"
            };
            series.OnPointMeasured(point =>
            {
                if (point.Visual is { } visual)
                {
                    visual.Fill = point.Model?.IsMuted == true
                        ? irrelevantPaint
                        : null;
                }
            });
            return (ISeries)series;
        }).ToArray();
        var foreground = ResolveColor("ForegroundColor", Colors.Gray);
        var muted = ResolveColor("MutedColor", Colors.Gray);
        Chart.XAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(ToSkColor(foreground, ChartColor.Text)),
                SeparatorsPaint = new SolidColorPaint(ToSkColor(muted, ChartColor.Grid), 1),
                MinLimit = 0,
                TextSize = 11
            }
        ];
        Chart.YAxes =
        [
            new Axis
            {
                Labels = rows.Select(row => row.ClientDisplay).ToArray(),
                LabelsPaint = new SolidColorPaint(ToSkColor(foreground, ChartColor.Text)),
                SeparatorsPaint = null,
                MinStep = 1,
                TextSize = 11
            }
        ];

        Chart.LegendTextPaint = new SolidColorPaint(ToSkColor(
            foreground,
            ChartColor.Text));
        ApplyTooltip();
    }

    private EntryRow[] BuildRows()
    {
        var rows = Items
            .Where(row => row.State is TraceEntryState.Success or TraceEntryState.Warning or TraceEntryState.Failed)
            .GroupBy(row => row.ClientDisplay, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EntryRow(
                group.Select(row => row.ClientRaw).ToHashSet(StringComparer.OrdinalIgnoreCase),
                group.Key,
                group.GroupBy(row => row.State).ToDictionary(state => state.Key, state => state.Sum(row => row.Count)),
                group.Sum(row => row.Count)))
            .OrderByDescending(row => row.Total)
            .ToArray();
        if (rows.Length <= 7)
        {
            return rows;
        }

        var selected = _selectedClient is null
            ? null
            : rows.FirstOrDefault(row => row.ClientRaws.Contains(_selectedClient));
        var visible = selected is null || rows.Take(6).Any(row => row.ClientRaws.Contains(_selectedClient!))
            ? rows.Take(6).ToArray()
            : rows.Take(5).Append(selected).ToArray();
        var visibleClients = visible.Select(row => row.ClientDisplay).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = rows.Where(row => !visibleClients.Contains(row.ClientDisplay)).ToArray();
        var otherCounts = remaining
            .SelectMany(row => row.Counts)
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Sum(pair => pair.Value));

        return visible.Append(new EntryRow(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            "其他",
            otherCounts,
            otherCounts.Values.Sum())).ToArray();
    }

    private Color StateColor(TraceEntryState state)
        => state switch
        {
            TraceEntryState.Success => ResolveColor("ChartStatusDoneColor", Color.FromRgb(163, 230, 53)),
            TraceEntryState.Warning => ResolveColor("ChartStatusWarningColor", Color.FromRgb(251, 146, 60)),
            TraceEntryState.Failed => ResolveColor("ChartStatusDangerColor", Color.FromRgb(244, 63, 94)),
            _ => ResolveColor("ChartStatusMutedColor", Color.FromRgb(148, 163, 184))
        };

    private static string StateName(TraceEntryState state)
        => state switch
        {
            TraceEntryState.Success => "成功",
            TraceEntryState.Warning => "部分成功",
            TraceEntryState.Failed => "失败",
            _ => "未知"
        };

    private sealed record EntryRow(
        IReadOnlySet<string> ClientRaws,
        string ClientDisplay,
        IReadOnlyDictionary<TraceEntryState, long> Counts,
        long Total);

    internal sealed record EntryPoint(long Value, bool IsMuted, string ClientDisplay);
}

internal static class ChartZoom
{
    internal static void Scale(Axis axis, double fullMin, double fullMax, double scale, double minSpan)
    {
        var currentMin = axis.MinLimit ?? fullMin;
        var currentMax = axis.MaxLimit ?? fullMax;
        var fullSpan = fullMax - fullMin;
        if (fullSpan <= 0)
        {
            return;
        }

        var targetSpan = Math.Clamp((currentMax - currentMin) * scale, minSpan, fullSpan);
        var center = (currentMin + currentMax) * 0.5;
        var targetMin = center - targetSpan * 0.5;
        var targetMax = center + targetSpan * 0.5;
        if (targetMin < fullMin)
        {
            targetMax += fullMin - targetMin;
            targetMin = fullMin;
        }
        else if (targetMax > fullMax)
        {
            targetMin -= targetMax - fullMax;
            targetMax = fullMax;
        }

        axis.MinLimit = targetMin;
        axis.MaxLimit = targetMax;
    }
}

public abstract class DashboardChart<TItem, TChart> : UserControl
    where TChart : Control
{
    private readonly IEnumerable<TItem> _items;
    private INotifyCollectionChanged? _observableItems;
    private bool _rebuildQueued;

    protected DashboardChart(IEnumerable<TItem> items, TChart chart)
    {
        _items = items;
        Chart = chart;
        Content = chart;
        ActualThemeVariantChanged += (_, _) => QueueRebuild();
    }

    protected IEnumerable<TItem> Items => _items;

    protected TChart Chart { get; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeItems();
        RefreshChart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeItems();
        base.OnDetachedFromVisualTree(e);
    }

    protected abstract void RebuildChart();

    protected Color ResolveColor(string key, Color fallback)
        => ThemeBrushResolver.TryGetColor(key, out var color) ? color : fallback;

    protected void ApplyTooltip()
    {
        var background = ResolveColor("CardBackgroundColor", Color.FromRgb(30, 30, 30));
        var foreground = ResolveColor("ForegroundColor", Colors.White);
        Chart.SetValue(CartesianChart.TooltipBackgroundPaintProperty,
            new SolidColorPaint(ToSkColor(background, ChartColor.Tooltip)));
        Chart.SetValue(CartesianChart.TooltipTextPaintProperty,
            new SolidColorPaint(ToSkColor(foreground, ChartColor.Label)));
        Chart.SetValue(CartesianChart.TooltipTextSizeProperty, 13d);
    }

    protected static double ParseNumber(string value)
    {
        if (double.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)
            || double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return 0;
    }

    protected static SKColor ToSkColor(Color color, byte alpha = byte.MaxValue)
        => ChartColor.ToSkColor(color, alpha);

    protected static string FormatPercent(double value, double total)
        => total <= 0 ? "0%" : (value / total).ToString("P1", CultureInfo.CurrentCulture);

    protected static string? NormalizeClient(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected static bool ClientEquals(string? left, string? right)
        => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private void SubscribeItems()
    {
        if (_observableItems is not null)
        {
            return;
        }

        _observableItems = _items as INotifyCollectionChanged;
        _observableItems?.CollectionChanged += OnCollectionChanged;
    }

    private void UnsubscribeItems()
    {
        _observableItems?.CollectionChanged -= OnCollectionChanged;
        _observableItems = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueRebuild();

    private void RefreshChart()
    {
        RebuildChart();
        Chart.InvalidateMeasure();
        Chart.InvalidateVisual();
    }

    protected void QueueRebuild()
    {
        if (_rebuildQueued)
        {
            return;
        }

        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _rebuildQueued = false;
            RefreshChart();
        }, DispatcherPriority.Background);
    }
}

internal static class ChartColor
{
    internal const byte Solid = 232;
    internal const byte Strong = 214;
    internal const byte Shape = 198;
    internal const byte Tooltip = 210;
    internal const byte Label = 238;
    internal const byte Text = 196;
    internal const byte Muted = 156;
    internal const byte Grid = 38;
    internal const byte AreaTop = 112;
    internal const byte AreaBottom = 4;
    internal const byte Crosshair = 176;

    internal static SKColor ToSkColor(Color color, byte alpha = byte.MaxValue)
    {
        var combinedAlpha = (byte)((color.A * alpha) / byte.MaxValue);
        return new SKColor(color.R, color.G, color.B, combinedAlpha);
    }
}
