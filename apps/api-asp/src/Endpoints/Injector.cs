using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Api.Endpoints;

/// <summary>Injector 专用：预留库存与仓库任务回写</summary>
public static class InjectorEndpoints
{
    public static IEndpointRouteBuilder MapInjector(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v1/injector/txn/reserve", Reserve).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/injector/txn/commit", Commit).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/injector/txn/rollback", Rollback).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/injector/txn/cleanup", Cleanup).RequireAuthorization(AuthPolicies.Write);

        routes.MapPost("/v1/injector/msfx/claim", Claim).RequireAuthorization(AuthPolicies.Write);
        routes.MapGet("/v1/injector/msfx/warehouse-success", WarehouseSuccess)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/injector/msfx/tasks/{id:long}/pending-codes", PendingCodes)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/injector/msfx/tasks/{id:long}/codes", UpdateCodes)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/injector/msfx/staging/status", UpdateStaging)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/injector/msfx/tasks/{id:long}/events", InsertEvent)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/injector/msfx/tasks/{id:long}/finalize", FinalizeTask)
            .RequireAuthorization(AuthPolicies.Write);
        return routes;
    }

    private static Task<IResult> Reserve(
        HttpContext http,
        IInjectorService injector,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteJson(
            http,
            db,
            dedup,
            operation: "injector.txn.reserve",
            PacJsonContext.Default.InjectorReserveRequest,
            PacJsonContext.Default.InjectorReserveResult,
            (request, token) => injector.ReserveAsync(request, token),
            ct);

    private static Task<IResult> Commit(
        HttpContext http,
        IInjectorService injector,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteTxn(
            http,
            db,
            dedup,
            operation: "injector.txn.commit",
            injector.CommitAsync,
            ct);

    private static Task<IResult> Rollback(
        HttpContext http,
        IInjectorService injector,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteTxn(
            http,
            db,
            dedup,
            operation: "injector.txn.rollback",
            injector.RollbackAsync,
            ct);

    private static Task<IResult> Cleanup(
        HttpContext http,
        IInjectorService injector,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteJson(
            http,
            db,
            dedup,
            operation: "injector.txn.cleanup",
            PacJsonContext.Default.InjectorCleanupRequest,
            PacJsonContext.Default.InjectorCleanupResult,
            (request, token) => injector.CleanupPendingAsync(request.TimeoutMinutes, request.MaxBatch, token),
            ct);

    private static Task<IResult> Claim(
        HttpContext http,
        IInjectorService injector,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteJson(
            http,
            db,
            dedup,
            operation: "injector.msfx.claim",
            PacJsonContext.Default.InjectorClaimRequest,
            PacJsonContext.Default.InjectorClaimResult,
            (request, token) => injector.ClaimByTargetAsync(
                request.ClientId,
                request.DrugId,
                request.Spec,
                token),
            ct);

    private static async Task<IResult> WarehouseSuccess(
        HttpContext http,
        IInjectorService injector,
        CancellationToken ct)
    {
        var bill = http.Request.Query["warehouseBillNo"].ToString();
        var drugId = http.Request.Query["drugId"].ToString();
        var spec = http.Request.Query["spec"].ToString();
        var fingerprint = http.Request.Query["rowFingerprint"].ToString();
        if (string.IsNullOrWhiteSpace(bill)
            || string.IsNullOrWhiteSpace(drugId)
            || string.IsNullOrWhiteSpace(spec))
        {
            return ApiProblems.BadRequest(
                http,
                title: "Missing query",
                detail: "Query warehouseBillNo, drugId and spec are required");
        }

        var exists = await injector.HasWarehouseSuccessAsync(
                bill,
                drugId,
                spec,
                string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint,
                ct)
            .ConfigureAwait(false);
        return Results.Ok(new InjectorExistsResult(exists));
    }

    private static async Task<IResult> PendingCodes(
        HttpContext http,
        long id,
        IInjectorService injector,
        CancellationToken ct)
    {
        if (id <= 0)
        {
            return ApiProblems.BadRequest(http, title: "Invalid id", detail: "Path id must be positive");
        }

        var rows = await injector.GetPendingCodesAsync(id, ct).ConfigureAwait(false);
        return Results.Ok(rows);
    }

    private static async Task<IResult> UpdateCodes(
        HttpContext http,
        long id,
        IInjectorService injector,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        if (id <= 0)
        {
            return ApiProblems.BadRequest(http, title: "Invalid id", detail: "Path id must be positive");
        }

        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "injector.msfx.codes",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.InjectorCodesStatusRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    await injector.UpdateTaskCodesAsync(
                            id,
                            request.LeafCodes,
                            request.Status,
                            request.VerifyResult,
                            request.ErrMsg,
                            token)
                        .ConfigureAwait(false);
                    return CommandWrites.Json(
                        StatusCodes.Status200OK,
                        new InjectorOkResult(true, null),
                        PacJsonContext.Default.InjectorOkResult);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static Task<IResult> UpdateStaging(
        HttpContext http,
        IInjectorService injector,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteJson(
            http,
            db,
            dedup,
            operation: "injector.msfx.staging",
            PacJsonContext.Default.InjectorStagingStatusRequest,
            PacJsonContext.Default.InjectorOkResult,
            async (request, token) =>
            {
                await injector.UpdateStagingStatusAsync(
                        request.StagingIds,
                        request.CodeStatus,
                        request.ErrMsg,
                        token)
                    .ConfigureAwait(false);
                return new InjectorOkResult(true, null);
            },
            ct);

    private static async Task<IResult> InsertEvent(
        HttpContext http,
        long id,
        IInjectorService injector,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        if (id <= 0)
        {
            return ApiProblems.BadRequest(http, title: "Invalid id", detail: "Path id must be positive");
        }

        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "injector.msfx.event",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.InjectorEventRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    await injector.InsertEventAsync(
                            id,
                            request.Stage,
                            request.Level,
                            request.Message,
                            request.LeafCode,
                            token)
                        .ConfigureAwait(false);
                    return CommandWrites.Json(
                        StatusCodes.Status200OK,
                        new InjectorOkResult(true, null),
                        PacJsonContext.Default.InjectorOkResult);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> FinalizeTask(
        HttpContext http,
        long id,
        IInjectorService injector,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        if (id <= 0)
        {
            return ApiProblems.BadRequest(http, title: "Invalid id", detail: "Path id must be positive");
        }

        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "injector.msfx.finalize",
                requestBody: bodyBytes,
                action: async token =>
                {
                    InjectorFinalizeRequest? request = null;
                    if (bodyBytes.Length > 0
                        && (!CommandWrites.TryDeserialize(
                                bodyBytes,
                                PacJsonContext.Default.InjectorFinalizeRequest,
                                out request)
                            || request is null))
                    {
                        return CommandWrites.BadJson(http);
                    }

                    var result = await injector.FinalizeAsync(
                            id,
                            request?.ErrMsg,
                            request?.WarehouseBillNo,
                            request?.RowFingerprint,
                            token)
                        .ConfigureAwait(false);
                    return CommandWrites.Json(
                        StatusCodes.Status200OK,
                        result,
                        PacJsonContext.Default.InjectorFinalizeResult);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> WriteTxn(
        HttpContext http,
        IDb db,
        ICommandDedup dedup,
        string operation,
        Func<string, CancellationToken, Task<InjectorTxnResult>> action,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: operation,
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.InjectorTxnIdRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    var result = await action(request.TxnId, token).ConfigureAwait(false);
                    return CommandWrites.Json(
                        StatusCodes.Status200OK,
                        result,
                        PacJsonContext.Default.InjectorTxnResult);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> WriteJson<TRequest, TResult>(
        HttpContext http,
        IDb db,
        ICommandDedup dedup,
        string operation,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultInfo,
        Func<TRequest, CancellationToken, Task<TResult>> action,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: operation,
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(bodyBytes, requestInfo, out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    var result = await action(request, token).ConfigureAwait(false);
                    return CommandWrites.Json(StatusCodes.Status200OK, result, resultInfo);
                },
                ct)
            .ConfigureAwait(false);
    }
}
