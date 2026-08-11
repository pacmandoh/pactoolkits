using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Api.Endpoints;

/// <summary>追溯码查重与入库提交</summary>
public static class TraceCodesEndpoints
{
    public static IEndpointRouteBuilder MapTraceCodes(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v1/trace-codes/check-existing", CheckExisting)
            .RequireAuthorization(AuthPolicies.Write);
        routes.MapPost("/v1/trace-codes/submit", Submit)
            .RequireAuthorization(AuthPolicies.Write);
        return routes;
    }

    private static async Task<IResult> CheckExisting(
        HttpContext http,
        IScanCodeService scanCode,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteFreshAsync(
                http,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.TraceCodesExistingRequest,
                            out var request)
                        || request?.TraceCodes is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    var existing = await scanCode.FindExistingTraceCodesAsync(request.TraceCodes, token)
                        .ConfigureAwait(false);
                    var body = new TraceCodesExistingResponse(existing);
                    return CommandWrites.Json(
                        StatusCodes.Status200OK,
                        body,
                        PacJsonContext.Default.TraceCodesExistingResponse);
                },
                ct)
            .ConfigureAwait(false);
    }

    private static async Task<IResult> Submit(
        HttpContext http,
        IScanCodeService scanCode,
        IDb db,
        ICommandDedup dedup,
        CancellationToken ct)
    {
        var bodyBytes = await CommandWrites.ReadBodyAsync(http.Request, ct).ConfigureAwait(false);
        return await CommandWrites.ExecuteAsync(
                http,
                db,
                dedup,
                operation: "trace_codes.submit",
                requestBody: bodyBytes,
                action: async token =>
                {
                    if (!CommandWrites.TryDeserialize(
                            bodyBytes,
                            PacJsonContext.Default.ScanCodeSubmitRequest,
                            out var request)
                        || request?.Analysis is null
                        || request.ValidUniqueCodes is null)
                    {
                        return CommandWrites.BadJson(http);
                    }

                    try
                    {
                        var result = await scanCode.SubmitAsync(request, token).ConfigureAwait(false);
                        return CommandWrites.Json(
                            StatusCodes.Status200OK,
                            result,
                            PacJsonContext.Default.ScanCodeSubmitResult);
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
