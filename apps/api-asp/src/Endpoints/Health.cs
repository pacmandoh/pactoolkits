using PacToolkits.Api.Hosting;

namespace PacToolkits.Api.Endpoints;

/// <summary>进程、PostgreSQL 与 schema 门禁的健康检查</summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealth(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/health", Check).AllowAnonymous();
        return routes;
    }

    private static async Task<IResult> Check(IApiHealth health, CancellationToken ct)
    {
        var snapshot = await health.CheckAsync(ct).ConfigureAwait(false);
        var body = new HealthResponse(
            snapshot.Ok ? "ok" : "unavailable",
            DateTimeOffset.UtcNow,
            snapshot.Database,
            snapshot.Schema,
            snapshot.SchemaVersion);

        return snapshot.Ok
            ? Results.Ok(body)
            : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

public sealed record HealthResponse(
    string Status,
    DateTimeOffset Utc,
    string Database,
    string Schema,
    string? SchemaVersion);
