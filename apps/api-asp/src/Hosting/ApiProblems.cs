using Microsoft.AspNetCore.Mvc;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Hosting;

/// <summary>业务错误的 ProblemDetails（code、traceId、可选 currentVersion）</summary>
public static class ApiProblems
{
    public static IResult BadRequest(
        HttpContext http,
        string title,
        string? detail = null,
        string code = ApiErrors.ValidationFailed)
        => Problem(http, StatusCodes.Status400BadRequest, code, title, detail);

    public static IResult NotFound(
        HttpContext http,
        string title,
        string? detail = null,
        string code = ApiErrors.NotFound)
        => Problem(http, StatusCodes.Status404NotFound, code, title, detail);

    public static IResult Conflict(
        HttpContext http,
        string title,
        string? detail = null,
        long? currentVersion = null,
        string code = ApiErrors.Conflict)
        => Problem(http, StatusCodes.Status409Conflict, code, title, detail, currentVersion);

    public static IResult ServiceUnavailable(
        HttpContext http,
        string title,
        string? detail = null,
        TimeSpan? retryAfter = null,
        string code = ApiErrors.ServiceUnavailable)
        => Problem(http, StatusCodes.Status503ServiceUnavailable, code, title, detail, retryAfter: retryAfter);

    public static IResult Problem(
        HttpContext http,
        int status,
        string code,
        string title,
        string? detail = null,
        long? currentVersion = null,
        TimeSpan? retryAfter = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (retryAfter is { } after && after > TimeSpan.Zero)
        {
            http.Response.Headers.RetryAfter = ((int)Math.Ceiling(after.TotalSeconds)).ToString();
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = ApiProblem.ResolveTraceId(http),
            },
        };

        if (currentVersion is { } version)
        {
            problem.Extensions["currentVersion"] = version;
        }

        return Results.Problem(problem);
    }

    /// <summary>读取 <see cref="PacApiHeaders.CommandId"/>；缺失或非法时 error 为 400</summary>
    public static bool TryGetCommandId(HttpRequest request, out Guid commandId, out IResult? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        commandId = default;
        error = null;

        if (!request.Headers.TryGetValue(PacApiHeaders.CommandId, out var values))
        {
            error = BadRequest(
                request.HttpContext,
                title: "Missing command id",
                detail: $"Header {PacApiHeaders.CommandId} is required for write commands");
            return false;
        }

        var raw = values.ToString();
        if (!Guid.TryParse(raw, out commandId) || commandId == Guid.Empty)
        {
            error = BadRequest(
                request.HttpContext,
                title: "Invalid command id",
                detail: $"Header {PacApiHeaders.CommandId} must be a non-empty UUID");
            return false;
        }

        return true;
    }
}
