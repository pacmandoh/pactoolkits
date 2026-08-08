using PacToolkits.Application.Abstractions;

namespace PacToolkits.Api.Hosting;

/// <summary>
/// SchemaBounds 阻断时：业务 IDb 相关路由返回 503（与 /health 一致）
///
/// 不拦：/health、换票、/v1/ping、/v1/system/*
/// </summary>
public sealed class DbAccessGateMiddleware
{
    private readonly RequestDelegate _next;

    public DbAccessGateMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context, IDbAccessGuard guard)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(guard);

        if (guard.IsBlocked && IsDataPlane(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new ApiProblem(
                    StatusCodes.Status503ServiceUnavailable,
                    ApiErrors.ServiceUnavailable,
                    guard.BlockReason ?? "database access blocked",
                    context.TraceIdentifier),
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    // /v1 默认拦业务路由；auth / ping / system 不拦
    private static bool IsDataPlane(PathString path)
    {
        if (!path.StartsWithSegments("/v1"))
        {
            return false;
        }

        if (path.StartsWithSegments("/v1/auth")
            || path.StartsWithSegments("/v1/ping")
            || path.StartsWithSegments("/v1/system"))
        {
            return false;
        }

        return true;
    }
}
