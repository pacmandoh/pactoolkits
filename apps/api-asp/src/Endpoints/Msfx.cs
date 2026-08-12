using System.Text;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Api.Endpoints;

/// <summary>MSFX 库侧业务：看板、游标、映射、注入</summary>
public static class MsfxEndpoints
{
    public static IEndpointRouteBuilder MapMsfx(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/msfx/board", GetBoard).RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/msfx/pull-batches", GetPullBatches).RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/msfx/cursor", GetCursor).RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/msfx/cursor/advance", AdvanceCursor).RequireAuthorization(AuthPolicies.Write);

        routes.MapGet("/v1/msfx/mapping/queue", GetMappingQueue).RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/msfx/mapping/apply", ApplyMapping).RequireAuthorization(AuthPolicies.Write);
        routes.MapGet("/v1/msfx/mapping-batch/groups", GetMappingGroups).RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/msfx/mapping-batch/preview", PreviewMappingBatch).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/mapping-batch/commit", CommitMappingBatch).RequireAuthorization(AuthPolicies.Write);

        routes.MapGet("/v1/msfx/inject/queue", GetInjectQueue).RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/msfx/inject/build", BuildInject).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/inject/{id:long}/reopen", ReopenInject).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/inject/{id:long}/discard", DiscardInject).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/inject/{id:long}/remap", RemapInject).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/inject/merge", MergeInject).RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/msfx/inject/{id:long}/split", SplitInject).RequireAuthorization(AuthPolicies.Write);
        routes.MapGet("/v1/msfx/inject/{id:long}/split-units", GetSplitUnits).RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/msfx/inject/{id:long}/split-codes", GetSplitCodes).RequireAuthorization(AuthPolicies.Read);
        routes.MapPost("/v1/msfx/inject/{id:long}/split-custom", SplitInjectCustom)
            .RequireAuthorization(AuthPolicies.Write);
        return routes;
    }

    private static async Task<IResult> GetBoard(ISyncService sync, CancellationToken ct)
        => Results.Ok(await sync.GetAutoBoardSnapshotAsync(ct).ConfigureAwait(false));

    private static async Task<IResult> GetPullBatches(ISyncService sync, int? limit, CancellationToken ct)
        => Results.Ok(await sync.GetRecentPullBatchesAsync(limit ?? 100, ct).ConfigureAwait(false));

    private static async Task<IResult> GetCursor(ISyncService sync, string? sourceApi, CancellationToken ct)
        => Results.Ok(await sync.GetPullCursorAsync(sourceApi ?? "listupout", ct).ConfigureAwait(false));

    private static async Task<IResult> AdvanceCursor(
        HttpContext http,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "msfx.cursor.advance",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.MsfxCursorAdvanceRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var cursor = await sync.AdvancePullCursorToAsync(
                                request.SourceApi,
                                request.Target,
                                token)
                            .ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            cursor,
                            PacJsonContext.Default.MsfxPullCursorState);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var entry))
                    {
                        return entry;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetMappingQueue(
        ISyncService sync,
        int? pageSize,
        string? mapStatuses,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        DateTimeOffset? cursorUpdatedAt,
        long? cursorId,
        bool? newer,
        bool? seekLastPage,
        CancellationToken ct)
    {
        var statuses = string.IsNullOrWhiteSpace(mapStatuses)
            ? null
            : mapStatuses.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var page = await sync.GetMappingQueuePageAsync(
                pageSize ?? 50,
                statuses,
                codeStatus,
                searchScope,
                keyword,
                cursorUpdatedAt,
                cursorId,
                newer ?? false,
                seekLastPage ?? false,
                ct)
            .ConfigureAwait(false);
        return Results.Ok(page);
    }

    private static async Task<IResult> ApplyMapping(
        HttpContext http,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "msfx.mapping.apply",
                requestBody: bodyBytes,
                action: async token =>
                {
                    var limit = 200;
                    if (bodyBytes.Length > 0)
                    {
                        if (!CommandWrites.TryDeserialize(
                                bodyBytes,
                                PacJsonContext.Default.MsfxMappingApplyRequest,
                                out var request)
                            || request is null)
                        {
                            return CommandWrites.BadJson(http);
                        }

                        limit = request.Limit;
                    }

                    var result = await sync.ApplyMappingAsync(limit, token).ConfigureAwait(false);
                    return CommandWrites.Json(
                        StatusCodes.Status200OK,
                        result,
                        PacJsonContext.Default.MsfxMapApplyResult);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetMappingGroups(
        ISyncService sync,
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        int? limit,
        CancellationToken ct)
        => Results.Ok(await sync.GetMappingBatchGroupsAsync(
                mapStatus,
                codeStatus,
                searchScope,
                keyword,
                limit ?? 200,
                ct)
            .ConfigureAwait(false));

    private static async Task<IResult> PreviewMappingBatch(
        HttpContext http,
        ISyncService sync,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteFreshAsync(
                http,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.MsfxMappingBatchRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var preview = await sync.PreviewMappingBatchByGroupAsync(
                                request.MapStatus,
                                request.CodeStatus,
                                request.SearchScope,
                                request.Keyword,
                                request.GroupSourceDrugNameRaw,
                                request.GroupSourceSpecRaw,
                                request.GroupSourceNameNorm,
                                request.GroupSourceSpecNorm,
                                request.Action,
                                request.DrugId,
                                request.Spec,
                                token)
                            .ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            preview,
                            PacJsonContext.Default.MsfxMappingBatchPreview);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var entry))
                    {
                        return entry;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> CommitMappingBatch(
        HttpContext http,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "msfx.mapping-batch.commit",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.MsfxMappingBatchRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var result = await sync.ApplyMappingBatchByGroupAsync(
                                request.MapStatus,
                                request.CodeStatus,
                                request.SearchScope,
                                request.Keyword,
                                request.GroupSourceDrugNameRaw,
                                request.GroupSourceSpecRaw,
                                request.GroupSourceNameNorm,
                                request.GroupSourceSpecNorm,
                                request.Action,
                                request.DrugId,
                                request.Spec,
                                token)
                            .ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            result,
                            PacJsonContext.Default.MsfxMappingBatchApplyResult);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var entry))
                    {
                        return entry;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetInjectQueue(ISyncService sync, int? limit, CancellationToken ct)
        => Results.Ok(await sync.GetInjectQueueAsync(limit ?? 0, ct).ConfigureAwait(false));

    private static async Task<IResult> BuildInject(
        HttpContext http,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "msfx.inject.build",
                requestBody: bodyBytes,
                action: async token =>
                {
                    var maxGroups = 200;
                    if (bodyBytes.Length > 0)
                    {
                        if (!CommandWrites.TryDeserialize(
                                bodyBytes,
                                PacJsonContext.Default.MsfxInjectBuildRequest,
                                out var request)
                            || request is null)
                        {
                            return CommandWrites.BadJson(http);
                        }

                        maxGroups = request.MaxGroups;
                    }

                    var result = await sync.BuildInjectsAsync(maxGroups, token).ConfigureAwait(false);
                    return CommandWrites.Json(
                        StatusCodes.Status200OK,
                        result,
                        PacJsonContext.Default.MsfxBuildInject);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static Task<IResult> ReopenInject(
        HttpContext http,
        long id,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => InjectReasonWrite(
            http,
            id,
            db,
            dedup,
            operation: "msfx.inject.reopen",
            action: (request, token) => sync.ReopenInjectAsync(id, request?.OperatorName, request?.Reason, token),
            typeInfo: PacJsonContext.Default.MsfxInjectReopen,
            ct);

    private static Task<IResult> DiscardInject(
        HttpContext http,
        long id,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => InjectReasonWrite(
            http,
            id,
            db,
            dedup,
            operation: "msfx.inject.discard",
            action: (request, token) => sync.DiscardInjectAsync(id, request?.OperatorName, request?.Reason, token),
            typeInfo: PacJsonContext.Default.MsfxInjectDiscard,
            ct);

    private static Task<IResult> RemapInject(
        HttpContext http,
        long id,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
        => InjectReasonWrite(
            http,
            id,
            db,
            dedup,
            operation: "msfx.inject.remap",
            action: (request, token) => sync.RemapInjectAsync(id, request?.OperatorName, request?.Reason, token),
            typeInfo: PacJsonContext.Default.MsfxInjectRemap,
            ct);

    private static async Task<IResult> InjectReasonWrite<T>(
        HttpContext http,
        long id,
        IDb db,
        ICommandDedup dedup,
        string operation,
        Func<MsfxInjectReasonRequest?, CancellationToken, Task<T>> action,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        // 摘要含 path id：同 CommandId 换任务不能当重放
        var digestBody = BuildIdPayload(id, bodyBytes);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: operation,
                requestBody: digestBody,
                action: async token =>
                {
                    MsfxInjectReasonRequest? request = null;
                    if (bodyBytes.Length > 0
                        && !CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.MsfxInjectReasonRequest,
                            out request))
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var result = await action(request, token).ConfigureAwait(false);
                        return CommandWrites.Json(StatusCodes.Status200OK, result, typeInfo);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var entry))
                    {
                        return entry;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> MergeInject(
        HttpContext http,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "msfx.inject.merge",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.MsfxInjectMergeRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var result = await sync.MergeInjectsAsync(
                                request.TaskIds,
                                request.OperatorName,
                                request.Reason,
                                token)
                            .ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            result,
                            PacJsonContext.Default.MsfxInjectMerge);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var entry))
                    {
                        return entry;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> SplitInject(
        HttpContext http,
        long id,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        var digestBody = BuildIdPayload(id, bodyBytes);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "msfx.inject.split",
                requestBody: digestBody,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.MsfxInjectSplitRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var result = await sync.SplitInjectAsync(
                                id,
                                request.SplitMode,
                                request.OperatorName,
                                request.Reason,
                                token)
                            .ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            result,
                            PacJsonContext.Default.MsfxInjectSplit);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var entry))
                    {
                        return entry;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GetSplitUnits(ISyncService sync, long id, CancellationToken ct)
        => Results.Ok(await sync.GetInjectSplitUnitsAsync(id, ct).ConfigureAwait(false));

    private static async Task<IResult> GetSplitCodes(ISyncService sync, long id, CancellationToken ct)
        => Results.Ok(await sync.GetInjectSplitCodeRowsAsync(id, ct).ConfigureAwait(false));

    private static async Task<IResult> SplitInjectCustom(
        HttpContext http,
        long id,
        ISyncService sync,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        var digestBody = BuildIdPayload(id, bodyBytes);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "msfx.inject.split-custom",
                requestBody: digestBody,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.MsfxInjectSplitCustomRequest,
                            out var request)
                        || request is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var result = await sync.SplitInjectCustomAsync(
                                id,
                                request.GroupKeys,
                                request.BucketIndexes,
                                request.OperatorName,
                                request.Reason,
                                token)
                            .ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            result,
                            PacJsonContext.Default.MsfxInjectSplitCustom);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var entry))
                    {
                        return entry;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    // 与 Drugs.BuildPathPayload 同口径：path 是命令载荷的一部分
    private static byte[] BuildIdPayload(long id, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{id}\n");
        var payload = new byte[prefix.Length + body.Length];
        prefix.CopyTo(payload, 0);
        body.CopyTo(payload, prefix.Length);
        return payload;
    }
}
