using System.Text;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;
using PacToolkits.Application.Services;

namespace PacToolkits.Api.Endpoints;

/// <summary>药品索引查询与写命令</summary>
public static class DrugsEndpoints
{
    private const int MaxSearchLimit = 2000;

    public static IEndpointRouteBuilder MapDrugs(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/drugs", Search)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/drugs/{drugId}/{spec}", GetByKey)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapPut("/v1/drugs/{drugId}/{spec}", Save)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapDelete("/v1/drugs/{drugId}/{spec}", Delete)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/drugs/key-fix/preview", PreviewKeyFix)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/drugs/key-fix/commit", CommitKeyFix)
            .RequireAuthorization(AuthPolicies.Write);
        return routes;
    }

    private static async Task<IResult> Search(
        HttpContext http,
        IDrugIndexService drugs,
        CancellationToken ct)
    {
        var keyword = http.Request.Query["keyword"].ToString();
        var limitRaw = http.Request.Query["limit"].ToString();
        var limit = 200;
        if (!string.IsNullOrWhiteSpace(limitRaw)
            && (!int.TryParse(limitRaw, out limit) || limit <= 0 || limit > MaxSearchLimit))
        {
            return ApiProblems.BadRequest(
                http,
                title: "Invalid query",
                detail: $"Query limit must be 1..{MaxSearchLimit}");
        }

        var result = await drugs.SearchAsync(keyword, limit, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetByKey(
        HttpContext http,
        string drugId,
        string spec,
        IDrugIndexService drugs,
        CancellationToken ct)
    {
        var drug = InputNormalizer.Normalize(drugId);
        var specKey = InputNormalizer.Normalize(spec);
        if (drug is null || specKey is null)
        {
            return ApiProblems.BadRequest(http, title: "Missing path", detail: "drugId and spec are required");
        }

        var row = await drugs.GetByKeyAsync(drug, specKey, ct).ConfigureAwait(false);
        return row is null
            ? ApiProblems.NotFound(http, title: "Drug not found", detail: $"{drug}/{specKey}")
            : Results.Ok(row);
    }

    private static async Task<IResult> Save(
        HttpContext http,
        string drugId,
        string spec,
        IDrugIndexService drugs,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var pathDrug = InputNormalizer.Normalize(drugId);
        var pathSpec = InputNormalizer.Normalize(spec);
        if (pathDrug is null || pathSpec is null)
        {
            return ApiProblems.BadRequest(http, title: "Missing path", detail: "drugId and spec are required");
        }

        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        // 摘要算上 path，同 CommandId 换 path 不能当重放
        var payload = BuildPathPayload(pathDrug, pathSpec, bodyBytes);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "drugs.save",
                requestBody: payload,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.DrugIndexSaveRequest,
                            out var request)
                        || request?.Dto is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    if (!string.Equals(pathDrug, request.Dto.DrugId, StringComparison.Ordinal)
                        || !string.Equals(pathSpec, request.Dto.Spec, StringComparison.Ordinal))
                    {
                        return CommandWrites.Problem(
                            http,
                            StatusCodes.Status400BadRequest,
                            "Path mismatch",
                            "Path drugId/spec must match body Dto");
                    }

                    var saved = await drugs.SaveAsync(request, token).ConfigureAwait(false);
                    if (saved.Outcome == DrugSaveOutcome.ConcurrencyConflict)
                    {
                        return CommandWrites.Problem(
                            http,
                            StatusCodes.Status409Conflict,
                            "Concurrency conflict",
                            "Row changed",
                            saved.Saved?.Version ?? saved.Concurrency?.Current?.Version);
                    }

                    var body = new DrugIndexSaveResponse(saved.Outcome, saved.Saved);
                    return CommandWrites.Json(
                        StatusCodes.Status200OK,
                        body,
                        PacJsonContext.Default.DrugIndexSaveResponse);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> Delete(
        HttpContext http,
        string drugId,
        string spec,
        IDrugIndexService drugs,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var pathDrug = InputNormalizer.Normalize(drugId);
        var pathSpec = InputNormalizer.Normalize(spec);
        if (pathDrug is null || pathSpec is null)
        {
            return ApiProblems.BadRequest(http, title: "Missing path", detail: "drugId and spec are required");
        }

        var versionRaw = http.Request.Query["expectedVersion"].ToString();
        if (string.IsNullOrWhiteSpace(versionRaw)
            || !long.TryParse(versionRaw, out var expectedVersion))
        {
            return ApiProblems.BadRequest(
                http,
                title: "Missing version",
                detail: "Query expectedVersion is required");
        }

        var digestBody = Encoding.UTF8.GetBytes($"{pathDrug}\n{pathSpec}\n{expectedVersion}");
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "drugs.delete",
                requestBody: digestBody,
                action: async token =>
                {
                    try
                    {
                        await drugs.DeleteAsync(pathDrug, pathSpec, expectedVersion, token)
                            .ConfigureAwait(false);
                        return CommandWrites.Empty(StatusCodes.Status204NoContent);
                    }
                    catch (DrugIndexConcurrencyException ex)
                    {
                        return CommandWrites.Problem(
                            http,
                            StatusCodes.Status409Conflict,
                            "Concurrency conflict",
                            "Row changed",
                            ex.Current?.Version);
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> PreviewKeyFix(
        HttpContext http,
        IDrugIndexService drugs,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteFreshAsync(
                http,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.DrugKeyFixPreviewRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var preview = await drugs.PreviewKeyFixAsync(
                                request.SourceDrugId,
                                request.SourceSpec,
                                request.TargetDrugId,
                                request.TargetSpec,
                                token)
                            .ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            preview,
                            PacJsonContext.Default.DrugKeyFixPreviewDto);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var mapped))
                    {
                        return mapped;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> CommitKeyFix(
        HttpContext http,
        IDrugIndexService drugs,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "drugs.key_fix.commit",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.DrugKeyFixRequest,
                            out var request)
                        || request?.Source is null
                        || request.Target is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var result = await drugs.ApplyKeyFixAsync(request, token).ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            result,
                            PacJsonContext.Default.DrugKeyFixCommitResult);
                    }
                    catch (DrugIndexConcurrencyException ex)
                    {
                        return CommandWrites.Problem(
                            http,
                            StatusCodes.Status409Conflict,
                            "Concurrency conflict",
                            "Row changed",
                            ex.Current?.Version);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var mapped))
                    {
                        return mapped;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static byte[] BuildPathPayload(string drugId, string spec, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{drugId}\n{spec}\n");
        var payload = new byte[prefix.Length + body.Length];
        prefix.CopyTo(payload, 0);
        body.CopyTo(payload, prefix.Length);
        return payload;
    }
}
