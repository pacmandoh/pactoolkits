using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.UiTests;

public sealed class DataGridToneTests
{
    [AvaloniaFact]
    public void Row_tone_applies_class_on_materialized_row()
    {
        using var mount = MountGrid(
            [new ToneRow(GridTone.Warning), new ToneRow(GridTone.None)],
            grid =>
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Name",
                    Binding = new Binding("Name"),
                    Width = new DataGridLength(200),
                });
                DataGridRowTone.SetEnabled(grid, true);
            });

        var rows = mount.Grid.GetVisualDescendants().OfType<DataGridRow>().OrderBy(static row => row.Index).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains("ToneWarning10", rows[0].Classes);
        Assert.DoesNotContain(DataCells(rows[0]).First().Classes, static c => c.StartsWith("Tone", StringComparison.Ordinal));
        Assert.DoesNotContain(rows[1].Classes, static c => c.StartsWith("Tone", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void Row_tone_updates_when_source_property_changes()
    {
        var row = new MutableToneRow { RowTone = GridTone.None };
        using var mount = MountGrid(
            [row],
            grid =>
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Name",
                    Binding = new Binding("Name"),
                    Width = new DataGridLength(200),
                });
                DataGridRowTone.SetEnabled(grid, true);
            });

        var materialized = Assert.Single(mount.Grid.GetVisualDescendants().OfType<DataGridRow>());
        row.RowTone = GridTone.Danger;
        Pump(mount.Window);

        Assert.Contains("ToneDanger10", materialized.Classes);
        Assert.DoesNotContain(DataCells(materialized).First().Classes, static c => c.StartsWith("Tone", StringComparison.Ordinal));
    }

    private static IEnumerable<DataGridCell> DataCells(DataGridRow row)
        => row.GetVisualDescendants()
            .OfType<DataGridCell>()
            .Where(static cell => cell.FindAncestorOfType<DataGridRowHeader>() is null);

    private static GridMount MountGrid(
        IReadOnlyList<object> rows,
        Action<DataGrid>? configure = null)
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

        var grid = new DataGrid
        {
            Width = 640,
            Height = 240,
            ItemsSource = rows,
        };
        grid.Classes.Add("SectionGrid");
        configure?.Invoke(grid);

        var window = new Window
        {
            Width = 680,
            Height = 280,
            Content = grid,
        };

        // 先完成 tone 配置再 Show，走 LoadingRow 上色管线
        window.Show();
        Pump(window);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

        return new GridMount(window, grid, resources, shadTheme, pacStyles);
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
    }

    private sealed class GridMount : IDisposable
    {
        private readonly ResourceInclude _resources;
        private readonly ShadUI.ShadTheme _shadTheme;
        private readonly StyleInclude _pacStyles;

        public GridMount(
            Window window,
            DataGrid grid,
            ResourceInclude resources,
            ShadUI.ShadTheme shadTheme,
            StyleInclude pacStyles)
        {
            Window = window;
            Grid = grid;
            _resources = resources;
            _shadTheme = shadTheme;
            _pacStyles = pacStyles;
        }

        public Window Window { get; }
        public DataGrid Grid { get; }

        public void Dispose()
        {
            Window.Close();
            var app = global::Avalonia.Application.Current;
            if (app is null)
            {
                return;
            }

            app.Styles.Remove(_pacStyles);
            app.Styles.Remove(_shadTheme);
            app.Resources.MergedDictionaries.Remove(_resources);
        }
    }

    private sealed class ToneRow(GridTone rowTone) : IRowTone
    {
        public string Name { get; } = "Row";
        public GridTone RowTone { get; } = rowTone;
    }

    private sealed class MutableToneRow : INotifyPropertyChanged, IRowTone
    {
        private GridTone _rowTone;

        public string Name { get; } = "Row";

        public GridTone RowTone
        {
            get => _rowTone;
            set
            {
                if (_rowTone == value)
                {
                    return;
                }

                _rowTone = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowTone)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
