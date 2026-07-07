using System;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class InventoryRefreshDefer
{
    public static bool IsDeferred(DateTimeOffset suppressUntilUtc, DateTimeOffset now, string? topic)
        => now < suppressUntilUtc
           && WatermarkActiveRefreshDeferPolicy.ShouldDeferInventoryActiveRefresh(topic);
}
