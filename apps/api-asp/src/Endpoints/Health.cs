namespace PacToolkits.Api.Endpoints;

/// <summary>进程存活</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/health", () =>
                Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
            .AllowAnonymous();
        return routes;
    }
}

public sealed record HealthResponse(string Status, DateTimeOffset Utc);
