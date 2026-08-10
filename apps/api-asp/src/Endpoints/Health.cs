using PacToolkits.Api.Hosting;

namespace PacToolkits.Api.Endpoints;

/// <summary>匿名探活：只返回 status，不露 schema 细节</summary>
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
        var body = new HealthResponse(snapshot.Ok ? "ok" : "unavailable");

        return snapshot.Ok
            ? Results.Ok(body)
            : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

public sealed record HealthResponse(string Status);
