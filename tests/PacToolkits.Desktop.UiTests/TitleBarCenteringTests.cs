using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Views;

namespace PacToolkits.Desktop.UiTests;

public sealed class TitleBarCenteringTests
{
    [AvaloniaFact]
    public void Anchor_stays_centered_when_surrounding_controls_width_changes()
    {
        var backAndForward = new Border { Width = 56 };
        var anchor = new Border
        {
            Width = 420,
            Height = 22,
        };
        var update = new Border { Width = 0 };
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            Children = { backAndForward, anchor, update },
        };
        Grid.SetColumn(anchor, 1);
        Grid.SetColumn(update, 2);

        var centerSlot = new Grid { Children = { content } };
        var rightControls = new Border { Width = 250 };
        var titleBar = new Grid
        {
            Height = 32,
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { centerSlot, rightControls },
        };
        Grid.SetColumn(rightControls, 1);

        var window = new Window
        {
            Width = 800,
            Height = 200,
            Content = titleBar,
        };

        try
        {
            using var centering = new TitleBarCentering(content, anchor, titleBar);
            window.Show();
            FlushLayout(window);
            AssertCentered(anchor, titleBar);

            rightControls.Width = 100;
            update.Width = 90;
            FlushLayout(window);
            AssertCentered(anchor, titleBar);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertCentered(Control content, Control titleBar)
    {
        var center = content.TranslatePoint(new Point(content.Bounds.Width / 2, 0), titleBar);
        Assert.NotNull(center);
        Assert.Equal(titleBar.Bounds.Width / 2, center.Value.X, precision: 3);
    }

    private static void FlushLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
