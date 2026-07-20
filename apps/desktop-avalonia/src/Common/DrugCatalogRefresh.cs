using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class DrugCatalogRefresh
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public static bool IsMissing(IReadOnlyList<OptionItem> catalog, string? drug)
    {
        var key = (drug ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return false;
        }

        return !catalog.Any(x => string.Equals(x.Raw, key, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<IReadOnlyList<OptionItem>> LoadAsync(
        ILookupCatalogService lookup,
        bool forceRefresh,
        CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(Timeout);
        return await LookupOptions.GetDrugOptionsAsync(lookup, linked.Token, forceRefresh)
            .ConfigureAwait(false);
    }
}
