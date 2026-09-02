using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Api.Endpoints;

/// <summary>追溯码条码取码与导出审计</summary>
public static class TraceBarcodesEndpoints
{
    public static IEndpointRouteBuilder MapTraceBarcodes(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v1/trace-barcodes/pick", Pick)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/trace-barcodes/audit", Audit)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/trace-barcodes/audit-log/clear", ClearAuditLog)
            .RequireAuthorization(AuthPolicies.Write);
        return routes;
    }

    private static async Task<IResult> Pick(
        HttpContext http,
        ITraceBarcodeService traceBarcode,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "trace_barcodes.pick",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.TraceBarcodePickApiRequest,
                            out var request)
                        || request?.Items is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var result = await traceBarcode.PickAsync(
                            new TraceBarcodePickRequest(
                                request.BatchId,
                                request.OperatorName,
                                request.Items,
                                request.ExcludeRecentDays,
                                request.ExcludeTraceCodes),
                            token)
                        .ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            result,
                            PacJsonContext.Default.TraceBarcodePickResult);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var mapped))
                    {
                        return mapped;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> Audit(
        HttpContext http,
        ITraceBarcodeService traceBarcode,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "trace_barcodes.audit",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.TraceBarcodeAuditApiRequest,
                            out var request)
                        || request?.Items is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var inserted = await traceBarcode.AuditAsync(
                            new TraceBarcodeAuditRequest(
                                request.BatchId,
                                request.Action,
                                request.OperatorName,
                                request.Items),
                            token)
                        .ConfigureAwait(false);
                        var body = new TraceBarcodeAuditApiResponse(inserted);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            body,
                            PacJsonContext.Default.TraceBarcodeAuditApiResponse);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var mapped))
                    {
                        return mapped;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> ClearAuditLog(
        HttpContext http,
        ITraceBarcodeService traceBarcode,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "trace_barcodes.audit_log.clear",
                requestBody: bodyBytes,
                action: async token =>
                {
                    try
                    {
                        var deleted = await traceBarcode.ClearAuditLogAsync(token).ConfigureAwait(false);
                        var body = new TraceBarcodeClearAuditApiResponse(deleted);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            body,
                            PacJsonContext.Default.TraceBarcodeClearAuditApiResponse);
                    }
                    catch (Exception ex) when (CommandWrites.TryMapBusinessException(http, ex, out var mapped))
                    {
                        return mapped;
                    }
                },
                ct)
            .ConfigureAwait(false);
    }
}
