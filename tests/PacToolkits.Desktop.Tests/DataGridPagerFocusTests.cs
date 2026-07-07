using Avalonia.Controls;
using Avalonia.Controls.Templates;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;

namespace PacToolkits.Desktop.Tests;

[Collection("Avalonia")]
public sealed class DataGridPagerFocusTests
{
    [Fact]
    public void PageIndex_change_clears_clear_target_selection()
    {
        AvaloniaTestHost.EnsureInitialized();
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

    [Fact]
    public void DeferredGridSlot_page_index_change_clears_mounted_grid()
    {
        AvaloniaTestHost.EnsureInitialized();
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

    [Fact]
    public void ClearFocusAndSelection_clears_selected_item()
    {
        AvaloniaTestHost.EnsureInitialized();
        var grid = new DataGrid
        {
            ItemsSource = new[] { "alpha", "beta" },
            SelectedIndex = 1,
        };

        DataGridInteractionHelper.ClearFocusAndSelection(grid);

        Assert.Null(grid.SelectedItem);
        Assert.Equal(-1, grid.SelectedIndex);
    }

    private sealed class DashboardTestContext;
}
