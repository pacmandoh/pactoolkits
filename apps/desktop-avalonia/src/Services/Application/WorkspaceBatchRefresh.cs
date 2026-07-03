using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class WorkspaceBatchRefresh
{
    public static async Task RunAsync(
        IEnumerable<AppPageBase> pages,
        AppPageBase? active,
        Func<AppPageBase, bool> canRefresh,
        Func<AppPageBase, Task<bool>> tryRefresh,
        DirtyPageTracker dirty)
    {
        foreach (var page in pages)
        {
            if (!canRefresh(page))
            {
                continue;
            }

            if (!ReferenceEquals(page, active))
            {
                dirty.Mark(page);
            }
        }

        if (active is null || !canRefresh(active))
        {
            return;
        }

        if (await tryRefresh(active).ConfigureAwait(true))
        {
            dirty.Clear(active);
        }
        else
        {
            dirty.Mark(active);
        }
    }
}
