using System.Security.Claims;
using PacToolkits.Api.Auth;

namespace PacToolkits.Api.Endpoints;

/// <summary>JWT 校验探针</summary>
public static class PingEndpoints
{
    public static IEndpointRouteBuilder MapPing(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/ping", (ClaimsPrincipal user) =>
            {
                var clientId = user.FindFirstValue(JwtTokenIssuer.ClientIdClaim);
                if (string.IsNullOrEmpty(clientId))
                {
                    return Results.Unauthorized();
                }

                return Results.Ok(new PingResponse("pong", clientId));
            })
            .RequireAuthorization();
        return routes;
    }
}

public sealed record PingResponse(string Status, string ClientId);
