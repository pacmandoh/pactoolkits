using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Contracts.Presentation;

namespace PacToolkits.Desktop.UiTests;

public sealed class DataGridFrozenColumnsTests
{
    [AvaloniaFact]
    public void Marked_column_moves_to_left_and_freezes()
    {
        var first = new DataGridTextColumn { Header = "First" };
        var sticky = new DataGridTextColumn { Header = "Sticky" };
        var last = new DataGridTextColumn { Header = "Last" };
        var grid = new DataGrid
        {
            Columns = { first, sticky, last },
        };

        DataGridFrozenColumns.SetIsFrozen(sticky, true);
        DataGridFrozenColumns.SetEnabled(grid, true);

        Assert.Equal(0, sticky.DisplayIndex);
        Assert.Equal(1, grid.FrozenColumnCount);
    }

    [AvaloniaFact]
    public void Multiple_marked_columns_keep_their_relative_order()
    {
        var first = new DataGridTextColumn { Header = "First" };
        var stickyA = new DataGridTextColumn { Header = "Sticky A" };
        var middle = new DataGridTextColumn { Header = "Middle" };
        var stickyB = new DataGridTextColumn { Header = "Sticky B" };
        var grid = new DataGrid
        {
            Columns = { first, stickyA, middle, stickyB },
        };

        DataGridFrozenColumns.SetIsFrozen(stickyA, true);
        DataGridFrozenColumns.SetIsFrozen(stickyB, true);
        DataGridFrozenColumns.SetEnabled(grid, true);

        Assert.Equal(0, stickyA.DisplayIndex);
        Assert.Equal(1, stickyB.DisplayIndex);
        Assert.Equal(2, grid.FrozenColumnCount);
    }

    [AvaloniaFact]
    public void Generated_index_column_is_frozen()
    {
        var grid = new DataGrid
        {
            Columns =
            {
                new DataGridTextColumn { Header = "Value" },
            },
        };

        DataGridFrozenColumns.SetEnabled(grid, true);
        DataGridIndexColumn.SetEnabled(grid, true);

        Assert.True(DataGridIndexColumn.IsIndexColumn(grid.Columns[0]));
        Assert.True(DataGridFrozenColumns.GetIsFrozen(grid.Columns[0]));
        Assert.Equal(1, grid.FrozenColumnCount);
    }

    [AvaloniaFact]
    public void Selection_column_has_select_all_header_and_freezes_between_index_and_status()
    {
        var rows = new[]
        {
            new SelectableRow(),
            new SelectableRow(),
        };
        var status = new DataGridTextColumn { Header = "状态" };
        var grid = new DataGrid
        {
            ItemsSource = rows,
            Columns =
            {
                status,
                new DataGridTextColumn { Header = "任务ID" },
            },
        };

        DataGridFrozenColumns.SetIsFrozen(status, true);
        DataGridFrozenColumns.SetEnabled(grid, true);
        DataGridIndexColumn.SetEnabled(grid, true);
        DataGridRowSelection.SetEnabled(grid, true);
        var selectionChangedCount = 0;
        DataGridRowSelection.AddSelectionChangedHandler(
            grid,
            (_, _) => selectionChangedCount++);

        var selection = Assert.Single(grid.Columns, DataGridRowSelection.IsSelectionColumn);
        var header = Assert.IsType<CheckBox>(selection.Header);

        Assert.True(DataGridIndexColumn.IsIndexColumn(grid.Columns.Single(column => column.DisplayIndex == 0)));
        Assert.Same(selection, grid.Columns.Single(column => column.DisplayIndex == 1));
        Assert.Same(status, grid.Columns.Single(column => column.DisplayIndex == 2));
        Assert.Equal(3, grid.FrozenColumnCount);
        Assert.True(header.IsThreeState);

        header.IsChecked = true;

        Assert.All(rows, row => Assert.True(row.IsSelected));
        Assert.Equal(2, DataGridRowSelection.GetSelectedCount(grid));
        Assert.Equal(1, selectionChangedCount);
    }

    [AvaloniaFact]
    public void Selection_behavior_tracks_items_source_bound_after_it_is_enabled()
    {
        var grid = new DataGrid
        {
            Columns =
            {
                new DataGridTextColumn { Header = "任务ID" },
            },
        };
        DataGridRowSelection.SetEnabled(grid, true);
        var observedSelectedCount = -1;
        DataGridRowSelection.AddSelectionChangedHandler(
            grid,
            (_, args) => observedSelectedCount = args.SelectedCount);

        var rows = new[]
        {
            new SelectableRow(),
            new SelectableRow(),
        };
        grid.ItemsSource = rows;

        var selection = Assert.Single(grid.Columns, DataGridRowSelection.IsSelectionColumn);
        var header = Assert.IsType<CheckBox>(selection.Header);

        rows[0].IsSelected = true;

        Assert.Equal(1, DataGridRowSelection.GetSelectedCount(grid));
        Assert.Equal(1, observedSelectedCount);
        Assert.Null(header.IsChecked);

        header.IsChecked = true;

        Assert.All(rows, row => Assert.True(row.IsSelected));
        Assert.Equal(2, DataGridRowSelection.GetSelectedCount(grid));
        Assert.Equal(2, observedSelectedCount);
        Assert.True(header.IsChecked);
    }

    [AvaloniaFact]
    public void Fractional_scroll_gestures_apply_exact_frozen_column_compensation()
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
            Width = 300,
            Height = 180,
            ItemsSource = new[] { "row" },
            Columns =
            {
                new DataGridTextColumn { Header = "First", Width = new DataGridLength(220) },
                new DataGridTextColumn { Header = "Second", Width = new DataGridLength(220) },
            },
        };
        grid.Classes.Add("SectionGrid");
        grid.Classes.Add("IndexedGrid");
        grid.Classes.Add("MsfxStickyColumns");

        var window = new Window
        {
            Width = 320,
            Height = 200,
            Content = grid,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            grid.InvalidateMeasure();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var presenter = grid.GetVisualDescendants()
                .OfType<DataGridRowsPresenter>()
                .Single();
            var scrollBar = grid.GetVisualDescendants()
                .OfType<ScrollBar>()
                .Single(control => control.Name == "PART_HorizontalScrollbar");
            var row = grid.GetVisualDescendants()
                .OfType<DataGridRow>()
                .Single();
            var cellsPresenter = row.GetVisualDescendants()
                .OfType<DataGridCellsPresenter>()
                .Single();

            Assert.Equal(ScrollBarVisibility.Auto, grid.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Visible, scrollBar.Visibility);

            grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Visible;
            window.UpdateLayout();
            Assert.True(scrollBar.IsVisible);

            presenter.RaiseEvent(new ScrollGestureEventArgs(1, new Vector(10.4, 0)));
            window.UpdateLayout();
            AssertFrozenContentAligned(row, cellsPresenter);

            var nativeTransform = Assert.IsType<TranslateTransform>(cellsPresenter.RenderTransform);
            nativeTransform.X += 0.5;
            Assert.NotEqual(0, row.Bounds.X + nativeTransform.X);

            DataGridFrozenColumns.AlignFrozenRowsToViewport(grid);
            AssertFrozenContentAligned(row, cellsPresenter);

            presenter.RaiseEvent(new ScrollGestureEventArgs(1, new Vector(10.4, 0)));
            window.UpdateLayout();
            AssertFrozenContentAligned(row, cellsPresenter);
        }
        finally
        {
            window.Close();
            app.Styles.Remove(pacStyles);
            app.Styles.Remove(shadTheme);
            app.Resources.MergedDictionaries.Remove(resources);
        }
    }

    [AvaloniaFact]
    public void Regular_section_grid_keeps_horizontal_scroll_and_sticky_disabled()
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
            Columns =
            {
                new DataGridTextColumn { Header = "Value" },
            },
        };
        grid.Classes.Add("SectionGrid");
        grid.Classes.Add("IndexedGrid");

        var window = new Window { Content = grid };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal(ScrollBarVisibility.Disabled, grid.HorizontalScrollBarVisibility);
            Assert.Equal(0, grid.FrozenColumnCount);
        }
        finally
        {
            window.Close();
            app.Styles.Remove(pacStyles);
            app.Styles.Remove(shadTheme);
            app.Resources.MergedDictionaries.Remove(resources);
        }
    }

    private static void AssertFrozenContentAligned(DataGridRow row, DataGridCellsPresenter cellsPresenter)
    {
        var transform = Assert.IsType<TranslateTransform>(cellsPresenter.RenderTransform);
        Assert.Equal(0, row.Bounds.X + transform.X, precision: 6);
    }

    private sealed class SelectableRow : ISelectableRow, INotifyPropertyChanged
    {
        private bool _isSelected;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
