using PacToolkits.Api.Auth;

namespace PacToolkits.Api.Endpoints;

/// <summary>API Key 换 JWT</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v1/auth/token", IssueToken)
            .AllowAnonymous()
            .RequireRateLimiting(AuthServiceExtensions.TokenRateLimitPolicy);
        return routes;
    }

    private static IResult IssueToken(
        HttpRequest request,
        JwtTokenIssuer issuer,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Auth.Token");
        var key = issuer.ReadApiKey(request);
        if (!issuer.TryMatchApiKey(key, out var clientId, out var scopes))
        {
            logger.LogWarning("auth.token_denied remote={Remote}", request.HttpContext.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        var (accessToken, expiresIn) = issuer.Issue(clientId, scopes);
        logger.LogInformation("auth.token_issued clientId={ClientId}", clientId);
        return Results.Ok(new TokenResponse(accessToken, "Bearer", expiresIn, clientId));
    }
}

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string ClientId);
