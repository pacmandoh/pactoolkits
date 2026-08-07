using PacToolkits.Api.Auth;

namespace PacToolkits.Api.Endpoints;

/// <summary>API Key → JWT 换票</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v1/auth/token", IssueToken).AllowAnonymous();
        return routes;
    }

    private static IResult IssueToken(HttpRequest request, JwtTokenIssuer issuer)
    {
        var key = issuer.ReadApiKey(request);
        if (!issuer.TryMatchApiKey(key, out var clientId))
        {
            return Results.Unauthorized();
        }

        var (accessToken, expiresIn) = issuer.Issue(clientId);
        return Results.Ok(new TokenResponse(accessToken, "Bearer", expiresIn, clientId));
    }
}

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string ClientId);
