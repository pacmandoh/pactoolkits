using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class InventoryStockRowRemainTests
{
    [Fact]
    public void Remain_over_qty_sets_validation_error()
    {
        var row = CreateRow(qty: 10, remain: 4);

        row.Remain = 11;

        Assert.True(row.HasRemainValidationError);
    }

    [Fact]
    public void Remain_at_qty_clears_validation_error()
    {
        var row = CreateRow(qty: 10, remain: 4);
        row.Remain = 11;

        row.Remain = 10;

        Assert.False(row.HasRemainValidationError);
    }

    private static StockRowItem CreateRow(int qty, int remain)
        => new(
            rowNo: 1,
            drugId: "a",
            spec: "s",
            traceCode: "t",
            qty,
            remain,
            status: 1,
            version: 0,
            isLow: false,
            isDeprecated: false);
}
