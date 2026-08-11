using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Support.Catalog;

/// <summary>下拉/查找选项构建</summary>
public static class LookupOptions
{
    public static async Task<IReadOnlyList<OptionItem>> GetDrugOptionsAsync(
        ILookupCatalogService lookup,
        CancellationToken ct,
        bool forceRefresh = false)
    {
        var drugs = await lookup.GetDrugIdsAsync(ct, forceRefresh).ConfigureAwait(false);
        return ToOptions(drugs);
    }

    public static async Task<IReadOnlyList<string>> GetSpecsAsync(
        ILookupCatalogService lookup,
        string? drug,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(drug))
        {
            return Array.Empty<string>();
        }

        return await lookup.GetSpecsByDrugAsync(drug, ct).ConfigureAwait(false);
    }

    public static async Task<(string? Drug, IReadOnlyList<string> Specs)> ResolveDrugAndSpecsAsync(
        ILookupCatalogService lookup,
        string? drugInput,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(drugInput))
        {
            return (null, Array.Empty<string>());
        }

        var canonical = await lookup.ResolveCanonicalDrugIdAsync(drugInput, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(canonical))
        {
            return (null, Array.Empty<string>());
        }

        var specs = await lookup.GetSpecsByDrugAsync(canonical, ct).ConfigureAwait(false);
        return (canonical, specs);
    }

    public static IReadOnlyList<OptionItem> ToOptions(IEnumerable<string> values)
        => values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new OptionItem(x, x))
            .ToArray();
}
