using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;
using PacToolkits.Application.Services;

namespace PacToolkits.Api.Endpoints;

/// <summary>库存查询与批量写命令</summary>
public static class InventoryEndpoints
{
    private const int MaxPageSize = 200;
    private const int MaxPageIndex = 10_000;

    public static IEndpointRouteBuilder MapInventory(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/inventory/stock", GetStock)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/inventory/drug-spec", GetDrugSpec)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/inventory/low-stock", GetLowStock)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/inventory/missing", GetMissing)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/inventory/target-exists", GetTargetExists)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/inventory/stock/edit", EditStock)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/inventory/stock/delete", DeleteStock)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/inventory/reassign/preview", PreviewReassign)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/inventory/reassign/commit", CommitReassign)
            .RequireAuthorization(AuthPolicies.Write);
        return routes;
    }

    private static Task<IResult> GetStock(
        HttpContext http,
        IInventoryOverviewService inventory,
        CancellationToken ct)
        => GetPageAsync(http, inventory.GetStockPageAsync, ct);

    private static Task<IResult> GetDrugSpec(
        HttpContext http,
        IInventoryOverviewService inventory,
        CancellationToken ct)
        => GetPageAsync(http, inventory.GetDrugSpecAggPageAsync, ct);

    private static Task<IResult> GetLowStock(
        HttpContext http,
        IInventoryOverviewService inventory,
        CancellationToken ct)
        => GetPageAsync(http, inventory.GetLowStockPageAsync, ct);

    private static Task<IResult> GetMissing(
        HttpContext http,
        IInventoryOverviewService inventory,
        CancellationToken ct)
        => GetPageAsync(http, inventory.GetMissingInventoryPageAsync, ct);

    private static async Task<IResult> GetTargetExists(
        HttpContext http,
        IInventoryOverviewService inventory,
        CancellationToken ct)
    {
        var drugId = InputNormalizer.Normalize(http.Request.Query["drugId"].ToString());
        var spec = InputNormalizer.Normalize(http.Request.Query["spec"].ToString());
        if (drugId is null || spec is null)
        {
            return ApiProblems.BadRequest(
                http,
                title: "Missing query",
                detail: "Query drugId and spec are required");
        }

        var exists = await inventory.TargetDrugSpecExistsAsync(drugId, spec, ct).ConfigureAwait(false);
        return Results.Ok(new InventoryTargetExistsResponse(exists));
    }

    private static async Task<IResult> EditStock(
        HttpContext http,
        IInventoryOverviewService inventory,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "inventory.stock.edit",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.InventoryStockEditRequest,
                            out var request)
                        || request?.Edits is null
                        || request.TraceCodeRule is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var result = await inventory.ApplyStockRowEditsAsync(
                                request.Edits,
                                request.TraceCodeRule,
                                token)
                            .ConfigureAwait(false);
                        if (result.Conflicts.Count > 0)
                        {
                            // conflicts 扩展字段供 Desktop 强制覆盖
                            return CommandWrites.Problem(
                                http,
                                StatusCodes.Status409Conflict,
                                "Concurrency conflict",
                                "One or more stock rows changed",
                                conflicts: result.Conflicts);
                        }

                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            result,
                            PacJsonContext.Default.StockRowEditBatchResult);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var mapped))
                    {
                        return mapped;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> DeleteStock(
        HttpContext http,
        IInventoryOverviewService inventory,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "inventory.stock.delete",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.InventoryStockDeleteRequest,
                            out var request)
                        || request?.TraceCodes is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var deleted = await inventory.DeleteStockByTraceCodesAsync(request.TraceCodes, token)
                            .ConfigureAwait(false);
                        var body = new InventoryStockDeleteResponse(deleted);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            body,
                            PacJsonContext.Default.InventoryStockDeleteResponse);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var mapped))
                    {
                        return mapped;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> PreviewReassign(
        HttpContext http,
        IInventoryOverviewService inventory,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteFreshAsync(
                http,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.InventoryReassignPreviewRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var preview = await inventory.PreviewReassignByKeywordAsync(
                                request.Keyword,
                                request.TargetDrugId,
                                request.TargetSpec,
                                request.TargetQty,
                                request.SampleLimit,
                                token)
                            .ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            preview,
                            PacJsonContext.Default.StockReassignPreviewDto);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var mapped))
                    {
                        return mapped;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> CommitReassign(
        HttpContext http,
        IInventoryOverviewService inventory,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "inventory.reassign.commit",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.InventoryReassignCommitRequest,
                            out var request)
                        || request?.Context is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    var hasKeyword = !string.IsNullOrWhiteSpace(request.Keyword);
                    var hasCodes = request.TraceCodes is { Count: > 0 };
                    if (hasKeyword == hasCodes)
                    {
                        return CommandWrites.Problem(
                            http,
                            StatusCodes.Status400BadRequest,
                            "Invalid body",
                            "Provide exactly one of keyword or traceCodes");
                    }

                    try
                    {
                        StockReassignApplyResultDto result;
                        if (hasCodes)
                        {
                            result = await inventory.ReassignByTraceCodesAsync(
                                    request.TraceCodes!,
                                    request.Context,
                                    token)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            result = await inventory.ReassignByKeywordAsync(
                                    request.Keyword!,
                                    request.Context,
                                    token)
                                .ConfigureAwait(false);
                        }

                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            result,
                            PacJsonContext.Default.StockReassignApplyResultDto);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var mapped))
                    {
                        return mapped;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetPageAsync<T>(
        HttpContext http,
        Func<string?, int, int, CancellationToken, Task<PagedResult<T>>> load,
        CancellationToken ct)
    {
        var keyword = http.Request.Query["keyword"].ToString();
        if (!TryBindPage(http.Request, out var page, out var pageSize, out var error))
        {
            return error!;
        }

        var result = await load(
                string.IsNullOrWhiteSpace(keyword) ? null : keyword,
                page,
                pageSize,
                ct)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static bool TryBindPage(
        HttpRequest request,
        out int page,
        out int pageSize,
        out IResult? error)
    {
        page = 1;
        pageSize = 50;
        error = null;

        var pageRaw = request.Query["page"].ToString();
        if (!string.IsNullOrWhiteSpace(pageRaw)
            && (!int.TryParse(pageRaw, out page) || page <= 0 || page > MaxPageIndex))
        {
            error = ApiProblems.BadRequest(
                request.HttpContext,
                title: "Invalid query",
                detail: $"Query page must be 1..{MaxPageIndex}");
            return false;
        }

        var sizeRaw = request.Query["pageSize"].ToString();
        if (!string.IsNullOrWhiteSpace(sizeRaw)
            && (!int.TryParse(sizeRaw, out pageSize) || pageSize <= 0 || pageSize > MaxPageSize))
        {
            error = ApiProblems.BadRequest(
                request.HttpContext,
                title: "Invalid query",
                detail: $"Query pageSize must be 1..{MaxPageSize}");
            return false;
        }

        return true;
    }
}
