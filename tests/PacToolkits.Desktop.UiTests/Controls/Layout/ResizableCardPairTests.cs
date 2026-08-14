using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.UiTests;

public sealed class ResizableCardPairTests
{
    [AvaloniaFact]
    public void Horizontal_pair_drag_changes_leading_column_width()
    {
        var pair = new ResizableCardPair
        {
            Width = 800,
            Height = 400,
            FirstMinSize = 320,
            SecondMinSize = 320,
            SplitterSize = 6,
            FirstContent = new CardPanel { IsResponsive = true },
            SecondContent = new CardPanel { IsResponsive = true },
        };

        var window = new Window
        {
            Width = 820,
            Height = 420,
            Content = pair,
        };

        try
        {
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var root = pair.FindControl<Grid>("LayoutRoot");
            var splitter = pair.FindControl<Border>("Splitter");
            Assert.NotNull(root);
            Assert.NotNull(splitter);

            var before = root.ColumnDefinitions[0].ActualWidth;
            var origin = splitter.TranslatePoint(
                new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2),
                window);
            Assert.NotNull(origin);

            window.MouseDown(origin.Value, MouseButton.Left);
            window.MouseMove(origin.Value + new Vector(80, 0));
            window.MouseUp(origin.Value + new Vector(80, 0), MouseButton.Left);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(root.ColumnDefinitions[0].Width.IsAbsolute);
            Assert.InRange(root.ColumnDefinitions[0].Width.Value, before + 40, before + 120);
        }
        finally
        {
            window.Close();
        }
    }
}
