namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class WatermarkActiveRefreshDeferPolicy
{
    public static bool ShouldDeferDrugIndexCascadeRefresh(string? topic)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();
        return key is "trace_pool" or "trace_txn" or "trace_txn_item";
    }

    public static bool ShouldDeferInventoryActiveRefresh(string? topic)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();
        return key is "inventory" or "trace_pool" or "trace_txn" or "trace_txn_item" or "";
    }
}
