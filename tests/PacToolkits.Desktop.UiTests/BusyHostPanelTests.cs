using Avalonia;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.UiTests;

public sealed class BusyHostPanelTests
{
    [AvaloniaFact]
    public void Busy_freezes_height_while_content_grows()
    {
        var growing = new Border { Width = 120, Height = 100 };
        var host = new BusyHostPanel { Width = 120 };
        host.Children.Add(growing);

        host.Measure(new Size(120, double.PositiveInfinity));
        host.IsBusy = true;
        host.Measure(new Size(120, double.PositiveInfinity));

        growing.Height = 420;
        host.InvalidateMeasure();
        host.Measure(new Size(120, double.PositiveInfinity));
        Assert.Equal(100, host.DesiredSize.Height);
    }
}
