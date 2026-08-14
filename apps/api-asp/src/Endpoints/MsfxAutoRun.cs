using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Api.Endpoints;

/// <summary>MSFX AutoRun 入库与跑锁</summary>
public static class MsfxAutoRunEndpoints
{
    public static IEndpointRouteBuilder MapMsfxAutoRun(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v1/msfx/autorun/lock", Lock).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/autorun/unlock", Unlock).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/autorun/renew", Renew).RequireAuthorization(AuthPolicies.Write);

        routes.MapPost("/v1/msfx/autorun/fail-interrupted", FailInterrupted)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapGet("/v1/msfx/autorun/window", GetWindow).RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/msfx/autorun/batches/start", StartBatch).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/autorun/batches/finish", FinishBatch).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/autorun/batches/request-id", UpdateRequestId)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/autorun/cursor/advance-window", AdvanceWindow)
            .RequireAuthorization(AuthPolicies.Write);

        routes.MapGet("/v1/msfx/autorun/retries", GetRetries).RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/msfx/autorun/retries/upsert", UpsertRetry).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/autorun/retries/succeeded", MarkRetrySucceeded)
            .RequireAuthorization(AuthPolicies.Write);

        routes.MapGet("/v1/msfx/autorun/watches", GetWatches).RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/msfx/autorun/watches/upsert", UpsertWatch).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/autorun/watches/resolved", MarkWatchResolved)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/autorun/watches/reschedule", RescheduleWatch)
            .RequireAuthorization(AuthPolicies.Write);

        routes.MapPost("/v1/msfx/autorun/bills/upsert", UpsertBill).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/autorun/bills/ingest", Ingest).RequireAuthorization(AuthPolicies.Write);
        return routes;
    }

    private static Task<IResult> Lock(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        CancellationToken ct)
        => FreshJson(
            http,
            PacJsonContext.Default.MsfxRunLockRequest,
            PacJsonContext.Default.MsfxRunLockResult,
            (request, token) => persist.TryAcquireLockAsync(request.SourceApi, token),
            ct);

    private static Task<IResult> Unlock(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        CancellationToken ct)
        => FreshAction(
            http,
            PacJsonContext.Default.MsfxRunLockReleaseRequest,
            async (request, token) =>
            {
                await persist.ReleaseLockAsync(request.SourceApi, request.LockId, token).ConfigureAwait(false);
                return CommandWrites.Empty(StatusCodes.Status204NoContent);
            },
            ct);

    private static Task<IResult> Renew(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        CancellationToken ct)
        => FreshAction(
            http,
            PacJsonContext.Default.MsfxRunLockReleaseRequest,
            async (request, token) =>
            {
                var ok = await persist.RenewLockAsync(request.SourceApi, request.LockId, token)
                    .ConfigureAwait(false);
                return ok
                    ? CommandWrites.Empty(StatusCodes.Status204NoContent)
                    : CommandWrites.Problem(
                        http,
                        StatusCodes.Status409Conflict,
                        "Lock not held",
                        "AutoRun lock is missing or expired");
            },
            ct);

    private static Task<IResult> FailInterrupted(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteJson(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.fail-interrupted",
            PacJsonContext.Default.MsfxFailInterruptedRequest,
            PacJsonContext.Default.MsfxCountResult,
            async (request, token) => new MsfxCountResult(
                await persist.FailInterruptedPullBatchesAsync(request.SourceApi, request.Error, token)
                    .ConfigureAwait(false)),
            ct);

    private static async Task<IResult> GetWindow(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        string? sourceApi,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceApi))
        {
            return ApiProblems.BadRequest(http, title: "Missing sourceApi", detail: "Query sourceApi is required");
        }

        if (!TryHold(http, persist, sourceApi))
        {
            return LockConflict(http);
        }

        var window = await persist.GetPullWindowAsync(sourceApi, ct).ConfigureAwait(false);
        return Results.Ok(window);
    }

    private static Task<IResult> StartBatch(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteJson(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.batch.start",
            PacJsonContext.Default.MsfxStartBatchRequest,
            PacJsonContext.Default.MsfxPullBatchStartResult,
            (request, token) => persist.StartPullBatchAsync(request.SourceApi, request.BeginAt, request.EndAt, token),
            ct);

    private static Task<IResult> FinishBatch(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteAction(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.batch.finish",
            PacJsonContext.Default.MsfxFinishBatchRequest,
            async (request, token) =>
            {
                await persist.FinishPullBatchAsync(
                        request.BatchId,
                        request.Status,
                        request.SuccessCount,
                        request.FailCount,
                        request.ErrMsg,
                        token)
                    .ConfigureAwait(false);
                return CommandWrites.Empty(StatusCodes.Status204NoContent);
            },
            ct);

    private static Task<IResult> UpdateRequestId(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteAction(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.batch.request-id",
            PacJsonContext.Default.MsfxBatchRequestIdRequest,
            async (request, token) =>
            {
                await persist.UpdatePullBatchRequestIdAsync(request.BatchId, request.RequestId, token)
                    .ConfigureAwait(false);
                return CommandWrites.Empty(StatusCodes.Status204NoContent);
            },
            ct);

    private static Task<IResult> AdvanceWindow(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteAction(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.cursor.advance-window",
            PacJsonContext.Default.MsfxAdvanceWindowRequest,
            async (request, token) =>
            {
                await persist.AdvancePullCursorAsync(
                        request.SourceApi,
                        request.BeginAt,
                        request.EndAt,
                        request.BatchId,
                        request.BatchStatus,
                        token)
                    .ConfigureAwait(false);
                return CommandWrites.Empty(StatusCodes.Status204NoContent);
            },
            ct);

    private static async Task<IResult> GetRetries(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        string? sourceApi,
        int? limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceApi))
        {
            return ApiProblems.BadRequest(http, title: "Missing sourceApi", detail: "Query sourceApi is required");
        }

        if (!TryHold(http, persist, sourceApi))
        {
            return LockConflict(http);
        }

        var rows = await persist.GetDueBillRetriesAsync(sourceApi, limit ?? 50, ct).ConfigureAwait(false);
        return Results.Ok(rows);
    }

    private static Task<IResult> UpsertRetry(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteAction(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.retry.upsert",
            PacJsonContext.Default.MsfxUpsertRetryRequest,
            async (request, token) =>
            {
                await persist.UpsertBillRetryAsync(
                        request.SourceApi,
                        request.BillCode,
                        request.FromRefUserId,
                        request.ToRefUserId,
                        request.LastError,
                        token)
                    .ConfigureAwait(false);
                return CommandWrites.Empty(StatusCodes.Status204NoContent);
            },
            ct);

    private static Task<IResult> MarkRetrySucceeded(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteAction(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.retry.succeeded",
            PacJsonContext.Default.MsfxBillCodeRequest,
            async (request, token) =>
            {
                await persist.MarkBillRetrySucceededAsync(request.SourceApi, request.BillCode, token)
                    .ConfigureAwait(false);
                return CommandWrites.Empty(StatusCodes.Status204NoContent);
            },
            ct);

    private static async Task<IResult> GetWatches(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        string? sourceApi,
        int? limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceApi))
        {
            return ApiProblems.BadRequest(http, title: "Missing sourceApi", detail: "Query sourceApi is required");
        }

        if (!TryHold(http, persist, sourceApi))
        {
            return LockConflict(http);
        }

        var rows = await persist.GetDueBillWatchesAsync(sourceApi, limit ?? 50, ct).ConfigureAwait(false);
        return Results.Ok(rows);
    }

    private static Task<IResult> UpsertWatch(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteAction(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.watch.upsert",
            PacJsonContext.Default.MsfxUpsertWatchRequest,
            async (request, token) =>
            {
                await persist.UpsertBillWatchAsync(
                        request.SourceApi,
                        request.BillCode,
                        request.FromRefUserId,
                        request.ToRefUserId,
                        request.FromEntName,
                        request.BillType,
                        request.BillTime,
                        request.BillUploadTime,
                        request.LastSeenStatus,
                        request.RawJson,
                        token)
                    .ConfigureAwait(false);
                return CommandWrites.Empty(StatusCodes.Status204NoContent);
            },
            ct);

    private static Task<IResult> MarkWatchResolved(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteAction(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.watch.resolved",
            PacJsonContext.Default.MsfxBillCodeRequest,
            async (request, token) =>
            {
                await persist.MarkBillWatchResolvedAsync(request.SourceApi, request.BillCode, token)
                    .ConfigureAwait(false);
                return CommandWrites.Empty(StatusCodes.Status204NoContent);
            },
            ct);

    private static Task<IResult> RescheduleWatch(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteAction(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.watch.reschedule",
            PacJsonContext.Default.MsfxRescheduleWatchRequest,
            async (request, token) =>
            {
                await persist.RescheduleBillWatchAsync(
                        request.SourceApi,
                        request.BillCode,
                        request.LastSeenStatus,
                        request.LastError,
                        token)
                    .ConfigureAwait(false);
                return CommandWrites.Empty(StatusCodes.Status204NoContent);
            },
            ct);

    private static Task<IResult> UpsertBill(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteJson(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.bill.upsert",
            PacJsonContext.Default.MsfxUpsertBillRequest,
            PacJsonContext.Default.MsfxUpsertBillResult,
            async (request, token) => new MsfxUpsertBillResult(
                await persist.UpsertInboundBillAsync(
                        request.BatchId,
                        request.BillCode,
                        request.BillType,
                        request.BillTime,
                        request.BillUploadTime,
                        request.FromRefUserId,
                        request.FromEntName,
                        request.ToRefUserId,
                        request.Status,
                        request.RawJson,
                        token)
                    .ConfigureAwait(false)),
            ct);

    private static Task<IResult> Ingest(
        HttpContext http,
        IMsfxAutoRunPersist persist,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => WriteJson(
            http,
            db,
            dedup,
            persist,
            operation: "msfx.autorun.bill.ingest",
            PacJsonContext.Default.MsfxIngestRequest,
            PacJsonContext.Default.MsfxIngestDetailResult,
            (request, token) => persist.IngestUpoutDetailAsync(request.BillId, request.BillCode, request.Drugs, token),
            ct);

    private static async Task<IResult> FreshJson<TRequest, TResult>(
        HttpContext http,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultInfo,
        Func<TRequest, CancellationToken, Task<TResult>> action,
        CancellationToken ct)
        => await FreshAction(
                http,
                requestInfo,
                async (request, token) =>
                {
                    var result = await action(request, token).ConfigureAwait(false);
                    return CommandWrites.Json(StatusCodes.Status200OK, result, resultInfo);
                },
                ct)
            .ConfigureAwait(false);

    private static async Task<IResult> FreshAction<TRequest>(
        HttpContext http,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestInfo,
        Func<TRequest, CancellationToken, Task<CommandDedupEntry>> action,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteFreshAsync(
                http,
                async token =>
                {
                    if (!CommandWrites.TryDeserialize(bodyBytes, requestInfo, out var request) || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    return await action(request, token).ConfigureAwait(false);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> WriteJson<TRequest, TResult>(
        HttpContext http,
        IDb db,
        ICommandDedup dedup,
        IMsfxAutoRunPersist persist,
        string operation,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultInfo,
        Func<TRequest, CancellationToken, Task<TResult>> action,
        CancellationToken ct)
        => await WriteAction(
                http,
                db,
                dedup,
                persist,
                operation,
                requestInfo,
                async (request, token) =>
                {
                    var result = await action(request, token).ConfigureAwait(false);
                    return CommandWrites.Json(StatusCodes.Status200OK, result, resultInfo);
                },
                ct)
            .ConfigureAwait(false);

    private static async Task<IResult> WriteAction<TRequest>(
        HttpContext http,
        IDb db,
        ICommandDedup dedup,
        IMsfxAutoRunPersist persist,
        string operation,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TRequest> requestInfo,
        Func<TRequest, CancellationToken, Task<CommandDedupEntry>> action,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation,
                bodyBytes,
                async token =>
                {
                    if (!CommandWrites.TryDeserialize(bodyBytes, requestInfo, out var request) || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    if (!TryHold(http, persist, SourceOf(request)))
                    {
                        return CommandWrites.Problem(
                            http,
                            StatusCodes.Status409Conflict,
                            "Lock not held",
                            "AutoRun lock is missing or expired");
                    }

                    return await action(request, token).ConfigureAwait(false);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static bool TryHold(HttpContext http, IMsfxAutoRunPersist persist, string? sourceApi)
    {
        if (!http.Request.Headers.TryGetValue(PacApiHeaders.MsfxRunLock, out var values)
            || !Guid.TryParse(values.ToString(), out var lockId)
            || lockId == Guid.Empty)
        {
            return false;
        }

        return persist.TryHoldLock(lockId, sourceApi);
    }

    private static IResult LockConflict(HttpContext http)
        => ApiProblems.Conflict(
            http,
            title: "Lock not held",
            detail: "AutoRun lock is missing or expired");

    private static string? SourceOf<TRequest>(TRequest request) => request switch
    {
        MsfxFailInterruptedRequest r => r.SourceApi,
        MsfxStartBatchRequest r => r.SourceApi,
        MsfxAdvanceWindowRequest r => r.SourceApi,
        MsfxUpsertRetryRequest r => r.SourceApi,
        MsfxBillCodeRequest r => r.SourceApi,
        MsfxUpsertWatchRequest r => r.SourceApi,
        MsfxRescheduleWatchRequest r => r.SourceApi,
        _ => null,
    };
}
