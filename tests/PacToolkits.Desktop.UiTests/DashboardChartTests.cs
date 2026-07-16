using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.UiTests;

public sealed class DashboardChartTests
{
    [AvaloniaFact]
    public void Drug_chart_groups_series_by_client()
    {
        var chart = new DrugTrendChart
        {
            ItemsSource = Enumerable.Range(0, 12)
                .Select(index => new TrendDrugItem
                {
                    DisplayIndex = index + 1,
                    Name = $"Drug {index + 1}",
                    Sub = "10mg",
                    ClientDisplay = $"Client {index + 1}",
                    ValueText = (index + 1).ToString()
                })
                .ToArray()
        };
        var window = new Window { Content = chart };

        try
        {
            window.Show();
            var liveChart = chart.FindControl<CartesianChart>("Chart");
            Assert.NotNull(liveChart);
            Assert.Equal(12, liveChart.Series.Count());
            Assert.Equal(7, liveChart.Series
                .Select(series => SolidColor(Assert.IsType<ColumnSeries<double?>>(series).Fill))
                .Distinct()
                .Count());
            Assert.Equal(
                [(byte)255, (byte)20],
                liveChart.Series.Take(2).Select(series =>
                    SolidColor(Assert.IsType<ColumnSeries<double?>>(series).Fill).Red));
            AssertPaintIsTranslucent(liveChart.TooltipBackgroundPaint);
            Assert.Equal(ZoomAndPanMode.X | ZoomAndPanMode.NoZoomBySection, liveChart.ZoomMode);
            var axis = Assert.Single(liveChart.XAxes);
            Assert.Equal(-0.5, axis.MinLimit);
            Assert.Equal(9.5, axis.MaxLimit);
            chart.ZoomIn();
            Assert.Equal(8, axis.MaxLimit - axis.MinLimit);
            chart.ZoomOut();
            Assert.Equal(10, axis.MaxLimit - axis.MinLimit);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Transaction_chart_groups_series_by_status_without_x_axis_labels()
    {
        var chart = new TxnChart(Enumerable.Range(0, 12).Select(index => new TxnItem(
            index + 1,
            index + 1,
            index % 2 == 0 ? TxnBadge.Done : TxnBadge.Danger,
            $"Drug {index + 1}",
            "10mg",
            (index + 1).ToString(),
            new DateTimeOffset(2026, 7, 16, index, 30, 0, TimeSpan.FromHours(8)),
            $"07-16 {index:00}:30:00",
            "Client A")));

        ShowChart(chart, () =>
        {
            var liveChart = Assert.IsType<CartesianChart>(chart.Content);
            Assert.Equal(2, liveChart.Series.Count());
            Assert.All(liveChart.XAxes, axis => Assert.Null(axis.LabelsPaint));
            Assert.All(liveChart.Series, series =>
                Assert.IsType<LinearGradientPaint>(Assert.IsType<LineSeries<DateTimePoint>>(series).Fill));
            Assert.Equal(
                [(byte)163, (byte)244],
                liveChart.Series.Select(series =>
                    SolidColor(Assert.IsType<LineSeries<DateTimePoint>>(series).Stroke).Red));
            AssertPaintIsTranslucent(liveChart.TooltipBackgroundPaint);
            Assert.Equal(ZoomAndPanMode.X | ZoomAndPanMode.NoZoomBySection, liveChart.ZoomMode);
            Assert.All(liveChart.XAxes, axis => Assert.NotNull(axis.CrosshairPaint));
            Assert.All(liveChart.YAxes, axis => Assert.NotNull(axis.CrosshairPaint));
            var axis = Assert.Single(liveChart.XAxes);
            Assert.NotNull(axis.MinLimit);
            Assert.NotNull(axis.MaxLimit);
            var initialSpan = axis.MaxLimit.Value - axis.MinLimit.Value;
            chart.ZoomIn();
            Assert.Equal(initialSpan * 0.8, axis.MaxLimit!.Value - axis.MinLimit!.Value, 3);
            chart.ZoomOut();
            Assert.Equal(initialSpan, axis.MaxLimit!.Value - axis.MinLimit!.Value, 3);
        });
    }

    [AvaloniaFact]
    public void Client_chart_rebuilds_after_deferred_data_arrives()
    {
        var client = new ClientInfo("raw", "Client A", "machine", "user", "127.0.0.1", "Windows", "1.0");
        var secondClient = client with { Raw = "raw-b", Display = "Client B", Machine = "raw-b" };
        var items = new ObservableCollection<TopClientItem>();
        var chart = new ClientChart(items, selectedClient: null);

        ShowChart(chart, () =>
        {
            var liveChart = Assert.IsType<PieChart>(chart.Content);
            Assert.Empty(liveChart.Series);

            items.Add(new TopClientItem(1, client, "12"));
            items.Add(new TopClientItem(2, secondClient, "8"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, liveChart.Series.Count());
            Assert.Equal(
                [(byte)139, (byte)255],
                liveChart.Series.Select(series =>
                    SolidColor(Assert.IsType<PieSeries<double>>(series).Fill).Red));
            Assert.All(liveChart.Series, series =>
                AssertPaintIsTranslucent(Assert.IsType<PieSeries<double>>(series).Fill));
            chart.SetSelectedClient("RAW-B");
            Dispatcher.UIThread.RunJobs();

            var selectedSeries = liveChart.Series
                .Select(series => Assert.IsType<PieSeries<double>>(series))
                .ToArray();
            Assert.Equal(0, selectedSeries[0].Pushout);
            Assert.Equal(10, selectedSeries[1].Pushout);
            Assert.Equal((byte)100, SolidColor(selectedSeries[0].Fill).Red);
            Assert.Equal((byte)255, SolidColor(selectedSeries[1].Fill).Red);
            AssertPaintIsTranslucent(liveChart.TooltipBackgroundPaint);
        });
    }

    [AvaloniaFact]
    public void Entry_chart_materializes_as_stacked_rows()
    {
        var items = new ObservableCollection<EntryChartItem>();
        var chart = new EntryChart(items, selectedClient: null);

        ShowChart(chart, () =>
        {
            var liveChart = Assert.IsType<CartesianChart>(chart.Content);
            Assert.Equal(3, liveChart.Series.Count());

            var states = new[]
            {
                TraceEntryState.Success,
                TraceEntryState.Warning,
                TraceEntryState.Failed
            };
            for (var clientIndex = 0; clientIndex < states.Length; clientIndex++)
            {
                foreach (var state in states)
                {
                    items.Add(new EntryChartItem(
                        $"raw-{clientIndex}",
                        $"Client {clientIndex + 1}",
                        state,
                        clientIndex + 1));
                }
            }
            items.Add(new EntryChartItem("raw-alias", "Client 1", TraceEntryState.Success, 4));
            items.Add(new EntryChartItem("raw-ignored", "Client ignored", TraceEntryState.Info, 99));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, liveChart.Series.Count());
            Assert.Equal(FindingStrategy.CompareOnlyY, liveChart.FindingStrategy);
            Assert.Equal(3, liveChart.Series
                .Select(series => SolidColor(Assert.IsType<StackedRowSeries<EntryChart.EntryPoint?>>(series).Fill))
                .Distinct()
                .Count());
            Assert.Equal(
                [(byte)163, (byte)251],
                liveChart.Series.Take(2).Select(series =>
                    SolidColor(Assert.IsType<StackedRowSeries<EntryChart.EntryPoint?>>(series).Fill).Red));
            Assert.All(liveChart.Series, series =>
                AssertPaintIsTranslucent(Assert.IsType<StackedRowSeries<EntryChart.EntryPoint?>>(series).Fill));
            chart.SetSelectedClient("raw-0");
            Dispatcher.UIThread.RunJobs();

            var selectedSeries = liveChart.Series
                .Select(series => Assert.IsType<StackedRowSeries<EntryChart.EntryPoint?>>(series))
                .ToArray();
            Assert.All(selectedSeries, series =>
            {
                var points = Assert.IsAssignableFrom<IEnumerable<EntryChart.EntryPoint?>>(series.Values)
                    .Where(point => point is not null)
                    .Select(point => point!)
                    .ToArray();
                Assert.Single(points, point => !point.IsMuted);
                Assert.All(points.Where(point => point.ClientDisplay != "Client 1"), point => Assert.True(point.IsMuted));
            });
            Assert.Equal(3, Assert.Single(liveChart.YAxes).Labels!.Count);
            AssertPaintIsTranslucent(liveChart.TooltipBackgroundPaint);
        });
    }

    [AvaloniaFact]
    public void Deferred_host_waits_for_visible_layout_before_creating_chart()
    {
        var created = 0;
        var host = new DeferredChartHost
        {
            Width = 240,
            Height = 160,
            IsVisible = false,
            Factory = () =>
            {
                created++;
                return new Border();
            }
        };
        var window = new Window { Content = host };

        try
        {
            window.Show();
            host.IsActive = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(host.Content);

            host.IsVisible = true;
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(host.Content);
            Assert.Equal(1, created);
        }
        finally
        {
            window.Close();
        }
    }

    private static void ShowChart(Control chart, Action assert)
    {
        var window = new Window { Content = chart };

        try
        {
            window.Show();
            Assert.Same(chart, window.Content);
            assert();
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertPaintIsTranslucent(object? paint)
    {
        Assert.InRange(SolidColor(paint).Alpha, (byte)1, (byte)254);
    }

    private static SkiaSharp.SKColor SolidColor(object? paint)
        => Assert.IsType<SolidColorPaint>(paint).Color;

}
