using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;
using PacToolkits.Desktop.Avalonia.Views.Pages;

namespace PacToolkits.Desktop.UiTests;

public sealed class DataGridPagerFocusTests
{
    [AvaloniaFact]
    public void PageIndex_change_clears_clear_target_selection()
    {
        var grid = new DataGrid
        {
            ItemsSource = new[] { "alpha", "beta" },
            SelectedIndex = 0,
        };
        var pager = new DataGridPager
        {
            ClearTarget = grid,
            PageIndex = 1,
        };
        pager.PageIndex = 2;

        Assert.Null(grid.SelectedItem);
        Assert.Equal(-1, grid.SelectedIndex);
    }

    [AvaloniaFact]
    public void DeferredGridSlot_page_index_change_clears_mounted_grid()
    {
        DataGrid? grid = null;
        var slot = new DeferredGridSlot
        {
            GridTemplate = new FuncDataTemplate<DashboardTestContext>((_, _) => new DataGrid
            {
                ItemsSource = new[] { "alpha", "beta" },
            }),
            DataContext = new DashboardTestContext(),
            PagerPageIndex = 1,
        };
        slot.GridMounted += (_, mounted) => grid = mounted;

        slot.MountGrid();
        Assert.NotNull(grid);
        grid!.SelectedIndex = 0;

        slot.PagerPageIndex = 2;

        Assert.Null(grid.SelectedItem);
        Assert.Equal(-1, grid.SelectedIndex);
    }

    [AvaloniaFact]
    public void ClearNativeRowHighlight_clears_selected_item()
    {
        var grid = new DataGrid
        {
            ItemsSource = new[] { "alpha", "beta" },
            SelectedIndex = 1,
        };

        DataGridInteractionHelper.ClearNativeRowHighlight(grid);

        Assert.Null(grid.SelectedItem);
        Assert.Equal(-1, grid.SelectedIndex);
    }

    [AvaloniaFact]
    public void Auto_fetch_tab_resolves_deferred_cell_converter_resources()
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
            Source = new Uri("avares://PacToolkits.Desktop/Styles/PacTheme.Styles.axaml"),
        };

        app.Resources.MergedDictionaries.Add(resources);
        app.Styles.Add(shadTheme);
        app.Styles.Add(pacStyles);

        var view = new ScanCodeAutoFetch();
        var window = new global::Avalonia.Controls.Window
        {
            Width = 1200,
            Height = 800,
            Content = view,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var grid = view.FindControl<DataGrid>("AutoTaskGrid");
            Assert.NotNull(grid);
            grid.ItemsSource = new[]
            {
                new AutoFetchTaskItem("增量拉取", "每 30 分钟", AutoFetchState.Running, "测试资源解析"),
            };

            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(grid.GetVisualDescendants().OfType<DataGridRow>());
        }
        finally
        {
            window.Close();
            app.Styles.Remove(pacStyles);
            app.Styles.Remove(shadTheme);
            app.Resources.MergedDictionaries.Remove(resources);
        }
    }

    private sealed class DashboardTestContext;
}
