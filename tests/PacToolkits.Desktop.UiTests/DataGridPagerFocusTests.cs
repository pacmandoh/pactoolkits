using Avalonia.Controls;
using Avalonia.Controls.Templates;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.Controls;

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
    public void ClearFocusAndSelection_clears_selected_item()
    {
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
