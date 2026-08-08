using System.Reflection;
using Microsoft.Extensions.Options;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;

namespace PacToolkits.Api.Endpoints;

/// <summary>非敏感系统信息</summary>
public static class SystemInfoEndpoints
{
    public static IEndpointRouteBuilder MapSystemInfo(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/system/info", GetInfo)
            .RequireAuthorization(AuthPolicies.SystemStatus);
        return routes;
    }

    private static IResult GetInfo(IOptions<SchemaBoundsOptions> bounds)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        return Results.Ok(new SystemInfoResponse(
            Product: "pactoolkits-api",
            ApiVersion: version,
            MinDbSchema: bounds.Value.MinDbSchema,
            MaxDbSchema: bounds.Value.MaxDbSchema,
            Utc: DateTimeOffset.UtcNow));
    }
}

public sealed record SystemInfoResponse(
    string Product,
    string ApiVersion,
    string MinDbSchema,
    string MaxDbSchema,
    DateTimeOffset Utc);
