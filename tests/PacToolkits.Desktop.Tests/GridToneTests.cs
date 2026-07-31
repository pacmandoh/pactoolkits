using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Behaviors;
using PacToolkits.Desktop.Avalonia.Common;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class GridToneTests
{
    [Fact]
    public void Class_name_maps_tone()
    {
        Assert.Equal("ToneWarning10", GridToneClass.ToClassName(GridTone.Warning));
        Assert.Equal("ToneDanger10", GridToneClass.ToClassName(GridTone.Danger));
        Assert.Null(GridToneClass.ToClassName(GridTone.None));
    }

    [Theory]
    [InlineData(0, 10, GridTone.Danger)]
    [InlineData(3, 10, GridTone.Warning)]
    [InlineData(10, 10, GridTone.Warning)]
    [InlineData(12, 10, GridTone.None)]
    public void Stock_resolve_maps_remain_against_threshold(int remain, int threshold, GridTone expected)
        => Assert.Equal(expected, StockTone.Resolve(remain, threshold));

    [Fact]
    public void Detail_row_only_tones_empty_remain()
    {
        var empty = new StockRowItem(1, "a", "s", "t", qty: 10, remain: 0, status: 0, version: 0, isLow: true, isDeprecated: false);
        var partial = new StockRowItem(1, "a", "s", "t", qty: 10, remain: 4, status: 0, version: 0, isLow: false, isDeprecated: false);

        Assert.Equal(GridTone.Danger, empty.RowTone);
        Assert.Equal(GridTone.None, partial.RowTone);
    }

    [Fact]
    public void Agg_row_uses_threshold()
    {
        var zero = new DrugSpecAggRowItem(1, "a", "s", 1, 10, RemainSum: 0, WeekUsed: 5, Threshold: 5, IsLow: true, IsDeprecated: false);
        var low = new DrugSpecAggRowItem(1, "a", "s", 1, 10, RemainSum: 5, WeekUsed: 5, Threshold: 5, IsLow: true, IsDeprecated: false);
        var ok = new DrugSpecAggRowItem(1, "a", "s", 1, 10, RemainSum: 6, WeekUsed: 5, Threshold: 5, IsLow: false, IsDeprecated: false);

        Assert.Equal(GridTone.Danger, zero.RowTone);
        Assert.Equal(GridTone.Warning, low.RowTone);
        Assert.Equal(GridTone.None, ok.RowTone);
    }

    [Fact]
    public void Low_stock_row_uses_threshold()
    {
        var zero = new LowStockRowItem(1, "a", "s", RemainSum: 0, Threshold: 5, IsLow: true);
        var low = new LowStockRowItem(1, "a", "s", RemainSum: 3, Threshold: 5, IsLow: true);

        Assert.Equal(GridTone.Danger, zero.RowTone);
        Assert.Equal(GridTone.Warning, low.RowTone);
    }

    [Theory]
    [InlineData(null, GridTone.None)]
    [InlineData("", GridTone.None)]
    [InlineData("常规备注", GridTone.None)]
    [InlineData("弃用", GridTone.Danger)]
    [InlineData("已弃用药品", GridTone.Danger)]
    [InlineData("未拆零", GridTone.Warning)]
    [InlineData("整盒未拆零发货", GridTone.Warning)]
    [InlineData("未拆零；弃用", GridTone.Danger)]
    [InlineData("弃用 / 未拆零", GridTone.Danger)]
    public void Note_resolve_maps_keywords(string? note, GridTone expected)
        => Assert.Equal(expected, NoteTone.Resolve(note));

    [Fact]
    public void Drug_row_tones_from_effective_note()
    {
        var row = new DrugIndex.DrugRow(new DrugIndexDto(
            DrugId: "DrugA",
            Spec: "1g",
            Qty: 10,
            RuleKey: null,
            PreTc: null,
            Note: "未拆零",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: null,
            Version: 1));

        Assert.Equal(GridTone.Warning, row.RowTone);

        row.NotePreview = "未拆零；弃用";
        Assert.Equal(GridTone.Danger, row.RowTone);

        row.NotePreview = null;
        row.Note = "普通";
        Assert.Equal(GridTone.None, row.RowTone);
    }
}
