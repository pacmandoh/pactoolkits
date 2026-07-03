using System;
using System.Collections.Generic;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class InventoryStockOrderPolicy
{
    public static bool MatchTraceCodeOrder<TCurrent, TServer>(
        IReadOnlyList<TCurrent> current,
        IReadOnlyList<TServer> server,
        Func<TCurrent, string> currentTraceCode,
        Func<TServer, string> serverTraceCode)
    {
        if (current.Count != server.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(currentTraceCode(current[i]), serverTraceCode(server[i]), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
