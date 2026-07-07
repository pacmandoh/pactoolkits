using Avalonia.Data;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Tests;

public sealed class DataGridInteractionHelperRulesTests
{
    [Fact]
    public void SortPath_returns_reflection_binding_path()
    {
        var binding = new Binding("DrugName");

        Assert.Equal("DrugName", DataGridInteractionHelper.Rules.SortPath(binding));
    }

    [Fact]
    public void SortPath_returns_null_for_missing_binding()
        => Assert.Null(DataGridInteractionHelper.Rules.SortPath(null));

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void SelectionInsertIndex_places_after_index_column(bool indexColumnFirst, int expected)
        => Assert.Equal(expected, DataGridInteractionHelper.Rules.SelectionInsertIndex(indexColumnFirst));

    [Theory]
    [InlineData(true, false, DataGridIndexHeaderFace.ClearFilter)]
    [InlineData(false, true, DataGridIndexHeaderFace.ClearSort)]
    [InlineData(false, false, DataGridIndexHeaderFace.Default)]
    public void IndexHeaderFace_prefers_filter_over_sort(
        bool filterActive,
        bool hasActiveSort,
        DataGridIndexHeaderFace expected)
        => Assert.Equal(expected, DataGridInteractionHelper.Rules.IndexHeaderFace(filterActive, hasActiveSort));

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(2, 3, null)]
    public void SelectAllTriState_matches_row_selection_counts(int selected, int total, bool? expected)
        => Assert.Equal(expected, DataGridInteractionHelper.Rules.SelectAllTriState(selected, total));
}
