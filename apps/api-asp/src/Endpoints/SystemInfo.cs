using System.Reflection;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.DTOs;

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

    private static IResult GetInfo(TimeProvider time)
    {
        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? asm.GetName().Version?.ToString()
            ?? "unknown";

        return Results.Ok(new PacApiSystemInfo(
            Product: "pactoolkits-api",
            ApiVersion: version,
            ContractVersion: ApiContract.Version,
            Utc: time.GetUtcNow()));
    }

    private static async Task<IResult> GetStatus(IApiHealth health, TimeProvider time, CancellationToken ct)
    {
        var snap = await health.CheckAsync(ct).ConfigureAwait(false);
        var body = new PacApiSystemStatus(
            Status: snap.Ok ? "ok" : "unavailable",
            Utc: time.GetUtcNow(),
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
