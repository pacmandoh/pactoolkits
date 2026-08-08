using System.Diagnostics;
using System.Security.Claims;
using PacToolkits.Api.Auth;

namespace PacToolkits.Api.Hosting;

/// <summary>请求审计字段：traceId、clientId、method/path、status、耗时；禁止写认证头</summary>
public sealed class RequestAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestAuditMiddleware> _logger;

    public RequestAuditMiddleware(RequestDelegate next, ILogger<RequestAuditMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            var traceId = context.TraceIdentifier;
            var clientId = context.User.FindFirstValue(JwtTokenIssuer.ClientIdClaim) ?? "-";
            _logger.LogInformation(
                "http request method={Method} path={Path} status={Status} clientId={ClientId} traceId={TraceId} durationMs={DurationMs}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                clientId,
                traceId,
                sw.ElapsedMilliseconds);
        }
    }
}
