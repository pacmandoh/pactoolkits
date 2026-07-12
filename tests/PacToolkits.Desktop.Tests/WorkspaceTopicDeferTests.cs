using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class WorkspaceTopicDeferTests
{
    [Theory]
    [InlineData("trace_pool", true)]
    [InlineData("trace_txn", true)]
    [InlineData("trace_txn_item", true)]
    [InlineData("drug_index", false)]
    [InlineData("inventory", false)]
    [InlineData("msfx", false)]
    [InlineData("", false)]
    public void DrugIndex_cascade_refresh_defer_matrix(string topic, bool expected)
    {
        Assert.Equal(expected, WorkspaceTopicRefresh.DeferDrugIndex(topic));
    }

    [Theory]
    [InlineData("inventory", true)]
    [InlineData("trace_pool", true)]
    [InlineData("trace_txn", true)]
    [InlineData("trace_txn_item", true)]
    [InlineData("", true)]
    [InlineData("drug_index", false)]
    [InlineData("msfx", false)]
    public void Inventory_active_refresh_defer_matrix(string topic, bool expected)
    {
        Assert.Equal(expected, WorkspaceTopicRefresh.DeferInventory(topic));
    }
}
