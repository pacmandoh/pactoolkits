using Avalonia.Controls;
using Avalonia.Data;
using PacToolkits.Desktop.Avalonia.Behaviors;

namespace PacToolkits.Desktop.Tests;

public sealed class DataGridSortSupportTests
{
    [Fact]
    public void Apply_seeds_sort_member_path_from_reflection_binding()
    {
        var grid = new DataGrid();
        var column = new DataGridTextColumn
        {
            Binding = new Binding("DrugName"),
        };
        grid.Columns.Add(column);

        DataGridSortSupport.Apply(grid);

        Assert.True(grid.CanUserSortColumns);
        Assert.Equal("DrugName", column.SortMemberPath);
    }

    [Fact]
    public void Apply_leaves_existing_sort_member_path()
    {
        var grid = new DataGrid();
        var column = new DataGridTextColumn
        {
            Binding = new Binding("DrugName"),
            SortMemberPath = "CustomPath",
        };
        grid.Columns.Add(column);

        DataGridSortSupport.Apply(grid);

        Assert.Equal("CustomPath", column.SortMemberPath);
    }
}
