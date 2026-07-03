using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class InventoryStockOrderPolicyTests
{
    [Fact]
    public void MatchTraceCodeOrder_returns_true_when_trace_codes_align_by_index()
    {
        var current = new[] { "A", "B", "C" };
        var server = new[] { "A", "B", "C" };

        Assert.True(InventoryStockOrderPolicy.MatchTraceCodeOrder(
            current,
            server,
            static row => row,
            static row => row));
    }

    [Fact]
    public void MatchTraceCodeOrder_returns_false_when_order_differs()
    {
        var current = new[] { "A", "B", "C" };
        var server = new[] { "A", "C", "B" };

        Assert.False(InventoryStockOrderPolicy.MatchTraceCodeOrder(
            current,
            server,
            static row => row,
            static row => row));
    }

    [Fact]
    public void MatchTraceCodeOrder_returns_false_when_counts_differ()
    {
        var current = new[] { "A", "B" };
        var server = new[] { "A" };

        Assert.False(InventoryStockOrderPolicy.MatchTraceCodeOrder(
            current,
            server,
            static row => row,
            static row => row));
    }
}
