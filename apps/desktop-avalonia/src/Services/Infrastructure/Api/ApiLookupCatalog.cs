using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Serialization;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>经 PacApi 的药品目录</summary>
public sealed class ApiLookupCatalog : ILookupCatalogService
{
    private readonly PacApiClient _api;

    public ApiLookupCatalog(PacApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<IReadOnlyList<string>> GetDrugIdsAsync(CancellationToken ct, bool forceRefresh = false)
    {
        _ = forceRefresh;
        var body = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/catalog/drug-ids")),
                PacJsonContext.Default.StringListResponse,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty catalog drug-ids response");
        return body.Items;
    }

    public async Task<IReadOnlyList<string>> GetSpecsByDrugAsync(
        string drugId,
        CancellationToken ct,
        bool forceRefresh = false)
    {
        _ = forceRefresh;
        var key = InputNormalizer.Normalize(drugId)
                  ?? throw new ArgumentException("drugId is required", nameof(drugId));
        var path = "/v1/catalog/drugs/" + Uri.EscapeDataString(key) + "/specs";
        var body = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve(path)),
                PacJsonContext.Default.StringListResponse,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty catalog specs response");
        return body.Items;
    }

    public async Task<string?> ResolveCanonicalDrugIdAsync(
        string? input,
        CancellationToken ct,
        bool forceRefresh = false)
    {
        var key = InputNormalizer.Normalize(input);
        if (key is null)
        {
            return null;
        }

        var all = await GetDrugIdsAsync(ct, forceRefresh).ConfigureAwait(false);
        return all.FirstOrDefault(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int?> GetQtyAsync(
        string? drugId,
        string? spec,
        CancellationToken ct,
        bool forceRefresh = false)
    {
        _ = forceRefresh;
        var drug = InputNormalizer.Normalize(drugId);
        var specKey = InputNormalizer.Normalize(spec);
        if (drug is null || specKey is null)
        {
            return null;
        }

        var path = "/v1/catalog/drugs/"
                   + Uri.EscapeDataString(drug)
                   + "/"
                   + Uri.EscapeDataString(specKey)
                   + "/quantity";
        var body = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve(path)),
                PacJsonContext.Default.CatalogQuantityResponse,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty catalog quantity response");
        return body.Quantity;
    }

    public async Task<bool> IsDeprecatedDrugIdAsync(
        string? drugId,
        CancellationToken ct,
        bool forceRefresh = false)
    {
        _ = forceRefresh;
        var key = InputNormalizer.Normalize(drugId);
        if (key is null)
        {
            return false;
        }

        var path = "/v1/catalog/drugs/" + Uri.EscapeDataString(key) + "/deprecated";
        var body = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve(path)),
                PacJsonContext.Default.CatalogDeprecatedResponse,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty catalog deprecated response");
        return body.Deprecated;
    }

    public void InvalidateDrugCatalog()
    {
    }
}
