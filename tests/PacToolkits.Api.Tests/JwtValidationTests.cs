using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PacToolkits.Api.Auth;

namespace PacToolkits.Api.Tests;

public sealed class JwtValidationTests
{
    [Fact]
    public async Task Ping_rejects_expired_token()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ForgeToken(expires: DateTime.UtcNow.AddMinutes(-10));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_wrong_issuer()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ForgeToken(issuer: "wrong-issuer");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_wrong_audience()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ForgeToken(audience: "wrong-audience");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_wrong_signing_key()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ForgeToken(signingKey: "other-jwt-signing-key-32chars-min!!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_missing_client_id_claim()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ForgeToken(includeClientId: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_token_without_read_scope()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ForgeToken(scopes: [AuthPolicies.Write]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SystemInfo_requires_system_status_scope()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ForgeToken(scopes: [AuthPolicies.Read]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/system/info", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SystemInfo_accepts_system_status_scope()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/system/info", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string ForgeToken(
        DateTime? expires = null,
        string issuer = "pactoolkits-api-test",
        string audience = "pactoolkits-clients-test",
        string signingKey = ApiFactory.TestJwtSigningKey,
        bool includeClientId = true,
        IReadOnlyList<string>? scopes = null)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, ApiFactory.TestClientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        if (includeClientId)
        {
            claims.Add(new Claim(JwtTokenIssuer.ClientIdClaim, ApiFactory.TestClientId));
        }

        foreach (var scope in scopes ?? [AuthPolicies.Read, AuthPolicies.Write, AuthPolicies.SystemStatus])
        {
            claims.Add(new Claim(AuthPolicies.ScopeClaim, scope));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: (expires ?? now.AddMinutes(30)).AddMinutes(-60),
            expires: expires ?? now.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
