using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Controls;
using ShadUI;
using Window = Avalonia.Controls.Window;

namespace PacToolkits.Desktop.UiTests;

public sealed class DataGridScrollbarTests
{
    [AvaloniaFact]
    public void Section_grid_thumb_expands_without_changing_its_hit_area_or_table_layout()
    {
        var grid = new DataGrid
        {
            Width = 300,
            Height = 180,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            ItemsSource = Enumerable.Range(1, 30),
            Columns =
            {
                new DataGridTextColumn { Header = "First", Width = new DataGridLength(220) },
                new DataGridTextColumn { Header = "Second", Width = new DataGridLength(220) },
            },
        };
        grid.Classes.Add("SectionGrid");

        using var mount = MountStyled(grid);
        var window = mount.Window;
        var presenter = grid.GetVisualDescendants().OfType<DataGridRowsPresenter>().Single();
        var scrollBar = grid.GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(control => control.Name == "PART_HorizontalScrollbar");
        var thumb = scrollBar.GetVisualDescendants().OfType<Thumb>().Single();
        var hitArea = thumb.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Name == "ThumbHitArea");
        var indicator = thumb.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Name == "ThumbIndicator");
        var presenterBounds = presenter.Bounds;

        Assert.Equal(10, scrollBar.Height);
        Assert.Equal(10, thumb.Bounds.Height);
        Assert.Equal(thumb.Bounds.Size, hitArea.Bounds.Size);
        Assert.Equal(4, indicator.Bounds.Height);

        var hoverPoint = scrollBar.TranslatePoint(
            new Point(scrollBar.Bounds.Width / 2, scrollBar.Bounds.Height / 2),
            window);
        Assert.NotNull(hoverPoint);
        window.MouseMove(hoverPoint.Value);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.Equal(7, indicator.Bounds.Height);
        Assert.Equal(presenterBounds, presenter.Bounds);

        var hitPoints = new[] { 0.25, thumb.Bounds.Height / 2, thumb.Bounds.Height - 0.25 }
            .Select(y => hitArea.TranslatePoint(new Point(hitArea.Bounds.Width / 2, y), window))
            .Select(Assert.NotNull)
            .ToArray();
        foreach (var point in hitPoints)
        {
            var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
            Assert.True(ReferenceEquals(hit, thumb) || hit.GetVisualAncestors().Contains(thumb));
        }

        var initialValue = scrollBar.Value;
        var dragPoint = hitPoints[^1];
        window.MouseDown(dragPoint, MouseButton.Left);
        window.MouseMove(dragPoint + new Vector(30, -20));
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        Assert.Equal(7, indicator.Bounds.Height);
        Assert.True(scrollBar.Value > initialValue);
        Assert.Equal(presenterBounds, presenter.Bounds);

        window.MouseUp(dragPoint + new Vector(30, -20), MouseButton.Left);
    }

    [AvaloniaFact]
    public void Busy_area_stretch_grid_gets_horizontal_bar_on_first_items()
    {
        var rows = new ObservableCollection<string>();
        var grid = new DataGrid
        {
            ItemsSource = rows,
            Columns =
            {
                new DataGridTextColumn { Header = "First", Width = new DataGridLength(280) },
                new DataGridTextColumn { Header = "Second", Width = new DataGridLength(280) },
                new DataGridTextColumn { Header = "Third", Width = new DataGridLength(280) },
                new DataGridTextColumn { Header = "Fourth", Width = new DataGridLength(280) },
            },
        };
        grid.Classes.Add("SectionGrid");
        grid.Classes.Add("IndexedGrid");
        grid.Classes.Add("StickyIndexDisplayIndex");
        grid.Classes.Add("MsfxStickyColumns");

        var empty = new EmptyStatePanel { IsEmpty = true, Content = grid };
        var busy = new BusyArea { Content = empty };
        var host = new Grid { MinHeight = 0 };
        host.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        host.Children.Add(busy);

        using var mount = MountStyled(host, width: 640, height: 360);
        empty.IsEmpty = false;
        rows.Add("row");
        Dispatcher.UIThread.RunJobs();
        mount.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var scrollBar = grid.GetVisualDescendants()
            .OfType<ScrollBar>()
            .Single(control => control.Name == "PART_HorizontalScrollbar");
        Assert.True(scrollBar.IsVisible && scrollBar.Maximum > 0);
    }

    private static GridMount MountStyled(Control content, double width = 320, double height = 200)
    {
        var app = global::Avalonia.Application.Current
            ?? throw new InvalidOperationException("Headless Application was not created.");
        var baseUri = new Uri("avares://PacToolkits.Desktop/");
        var resources = new ResourceInclude(baseUri)
        {
            Source = new Uri("avares://PacToolkits.Desktop/Styles/PacTheme.axaml"),
        };
        var shadTheme = new ShadUI.ShadTheme();
        var pacStyles = new StyleInclude(baseUri)
        {
            Source = new Uri("avares://PacToolkits.Desktop/Styles/App.axaml"),
        };
        app.Resources.MergedDictionaries.Add(resources);
        app.Styles.Add(shadTheme);
        app.Styles.Add(pacStyles);

        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
        };
        var mount = new GridMount(app, window, resources, shadTheme, pacStyles);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            return mount;
        }
        catch
        {
            mount.Dispose();
            throw;
        }
    }

    private sealed class GridMount(
        global::Avalonia.Application app,
        Window window,
        ResourceInclude resources,
        ShadUI.ShadTheme shadTheme,
        StyleInclude pacStyles) : IDisposable
    {
        public Window Window { get; } = window;

        public void Dispose()
        {
            Window.Close();
            app.Styles.Remove(pacStyles);
            app.Styles.Remove(shadTheme);
            app.Resources.MergedDictionaries.Remove(resources);
        }
    }
}
