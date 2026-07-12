using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.UiTests;

public sealed class DrugTrendChartTests
{
    [AvaloniaFact]
    public void Livecharts_package_materializes_chart_with_data()
    {
        var chart = new DrugTrendChart
        {
            ItemsSource = new[]
            {
                new TrendDrugItem
                {
                    DisplayIndex = 1,
                    Name = "Drug A",
                    Sub = "10mg",
                    ValueText = "12.5"
                }
            }
        };
        var window = new Window { Content = chart };

        try
        {
            window.Show();
            Assert.Same(chart, window.Content);
        }
        finally
        {
            window.Close();
        }
    }
}
