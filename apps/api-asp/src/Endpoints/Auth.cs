using Microsoft.Extensions.Options;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Endpoints;

/// <summary>API Key 换 JWT；敏感操作口令校验</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/v1/auth/token", IssueToken)
            .AllowAnonymous()
            .RequireRateLimiting(AuthServiceExtensions.TokenRateLimitPolicy);
        routes.MapPost("/v1/auth/unlock/verify", VerifyUnlock)
            .RequireAuthorization(AuthPolicies.Write);
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
        return Results.Ok(new PacApiTokenResponse(accessToken, "Bearer", expiresIn, clientId));
    }

    private static IResult VerifyUnlock(
        HttpContext http,
        UnlockVerifyRequest? body,
        IOptions<AuthOptions> options,
        ILoggerFactory loggerFactory)
    {
        var stored = options.Value.UnlockPasswordHash?.Trim() ?? string.Empty;
        if (!ApiKeyHasher.IsSha256Hex(stored))
        {
            return ApiProblems.BadRequest(
                http,
                title: "Unlock password is not configured",
                detail: "Auth:UnlockPasswordHash is missing or invalid",
                code: ApiErrors.UnlockNotConfigured);
        }

        var presented = body?.Password?.Trim() ?? string.Empty;
        var match = presented.Length > 0
                    && ApiKeyHasher.FixedTimeEqualsHex(ApiKeyHasher.Hash(presented), stored);
        if (!match)
        {
            var logger = loggerFactory.CreateLogger("Auth.Unlock");
            logger.LogWarning("auth.unlock_denied remote={Remote}", http.Connection.RemoteIpAddress);
            return ApiProblems.BadRequest(
                http,
                title: "Unlock password mismatch",
                code: ApiErrors.UnlockMismatch);
        }

        return Results.NoContent();
    }
}
