using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Api.Endpoints;

/// <summary>药品目录只读查询</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalog(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/catalog/client-ids", GetClientIds)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/catalog/drug-ids", GetDrugIds)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/catalog/drugs/{drugId}/specs", GetSpecs)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/catalog/drugs/{drugId}/{spec}/quantity", GetQuantity)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/catalog/drugs/{drugId}/deprecated", GetDeprecated)
            .RequireAuthorization(AuthPolicies.Read);
        return routes;
    }

    private static async Task<IResult> GetClientIds(ILookupCatalogService lookup, CancellationToken ct)
    {
        var items = await lookup.GetClientIdsAsync(ct, forceRefresh: true).ConfigureAwait(false);
        return Results.Ok(new StringListResponse(items));
    }

    private static async Task<IResult> GetDrugIds(ILookupCatalogService lookup, CancellationToken ct)
    {
        var items = await lookup.GetDrugIdsAsync(ct, forceRefresh: true).ConfigureAwait(false);
        return Results.Ok(new StringListResponse(items));
    }

    private static async Task<IResult> GetSpecs(
        HttpContext http,
        string drugId,
        ILookupCatalogService lookup,
        CancellationToken ct)
    {
        var key = InputNormalizer.Normalize(drugId);
        if (key is null)
        {
            return ApiProblems.BadRequest(http, title: "Missing drugId", detail: "Path drugId is required");
        }

        var items = await lookup.GetSpecsByDrugAsync(key, ct, forceRefresh: true).ConfigureAwait(false);
        return Results.Ok(new StringListResponse(items));
    }

    private static async Task<IResult> GetQuantity(
        HttpContext http,
        string drugId,
        string spec,
        ILookupCatalogService lookup,
        CancellationToken ct)
    {
        var drug = InputNormalizer.Normalize(drugId);
        var specKey = InputNormalizer.Normalize(spec);
        if (drug is null || specKey is null)
        {
            return ApiProblems.BadRequest(
                http,
                title: "Missing path",
                detail: "Path drugId and spec are required");
        }

        var qty = await lookup.GetQtyAsync(drug, specKey, ct, forceRefresh: true).ConfigureAwait(false);
        return Results.Ok(new CatalogQuantityResponse(qty));
    }

    private static async Task<IResult> GetDeprecated(
        HttpContext http,
        string drugId,
        ILookupCatalogService lookup,
        CancellationToken ct)
    {
        var key = InputNormalizer.Normalize(drugId);
        if (key is null)
        {
            return ApiProblems.BadRequest(http, title: "Missing drugId", detail: "Path drugId is required");
        }

        var deprecated = await lookup.IsDeprecatedDrugIdAsync(key, ct, forceRefresh: true).ConfigureAwait(false);
        return Results.Ok(new CatalogDeprecatedResponse(deprecated));
    }
}
