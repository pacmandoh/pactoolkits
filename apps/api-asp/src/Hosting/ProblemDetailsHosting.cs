using System.Diagnostics;

namespace PacToolkits.Api.Hosting;

/// <summary>ProblemDetails 扩展字段 code 的稳定码</summary>
public static class ApiErrors
{
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string ServiceUnavailable = "service_unavailable";
    public const string InternalError = "internal_error";
    public const string RateLimited = "rate_limited";
}

public sealed record ApiProblem(
    int Status,
    string Code,
    string Title,
    string TraceId);

public static class ProblemDetailsHosting
{
    public static IServiceCollection AddPacToolkitsProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
                if (!ctx.ProblemDetails.Extensions.ContainsKey("code"))
                {
                    ctx.ProblemDetails.Extensions["code"] = ctx.ProblemDetails.Status switch
                    {
                        StatusCodes.Status401Unauthorized => ApiErrors.Unauthorized,
                        StatusCodes.Status403Forbidden => ApiErrors.Forbidden,
                        StatusCodes.Status404NotFound => ApiErrors.NotFound,
                        StatusCodes.Status409Conflict => ApiErrors.Conflict,
                        StatusCodes.Status429TooManyRequests => ApiErrors.RateLimited,
                        StatusCodes.Status503ServiceUnavailable => ApiErrors.ServiceUnavailable,
                        >= 400 and < 500 => ApiErrors.ValidationFailed,
                        _ => ApiErrors.InternalError,
                    };
                }
            };
        });
        return services;
    }

    public static WebApplication UsePacToolkitsProblemDetails(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}
