using System.Reflection;
using Microsoft.Extensions.Options;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;

namespace PacToolkits.Api.Endpoints;

/// <summary>需 system.status 的系统信息与诊断</summary>
public static class SystemInfoEndpoints
{
    public static IEndpointRouteBuilder MapSystemInfo(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/system/info", GetInfo)
            .RequireAuthorization(AuthPolicies.SystemStatus);
        routes.MapGet("/v1/system/status", GetStatus)
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

    private static async Task<IResult> GetStatus(IApiHealth health, CancellationToken ct)
    {
        var snap = await health.CheckAsync(ct).ConfigureAwait(false);
        var body = new SystemStatusResponse(
            Status: snap.Ok ? "ok" : "unavailable",
            Utc: DateTimeOffset.UtcNow,
            Database: snap.Database,
            Schema: snap.Schema,
            SchemaVersion: snap.SchemaVersion,
            Reason: snap.Ok ? null : Diagnose(snap));

        return snap.Ok
            ? Results.Ok(body)
            : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static string Diagnose(ApiHealthSnapshot snap)
    {
        if (!string.Equals(snap.Database, "ok", StringComparison.Ordinal))
        {
            return "database_unavailable";
        }

        return snap.Schema switch
        {
            "incompatible" => "schema_incompatible",
            "metadata_missing" => "schema_metadata_missing",
            "unavailable" => "schema_unavailable",
            _ => "unavailable",
        };
    }
}

public sealed record SystemInfoResponse(
    string Product,
    string ApiVersion,
    string MinDbSchema,
    string MaxDbSchema,
    DateTimeOffset Utc);

public sealed record SystemStatusResponse(
    string Status,
    DateTimeOffset Utc,
    string Database,
    string Schema,
    string? SchemaVersion,
    string? Reason);
