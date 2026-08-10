using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PacToolkits.Api.Auth;

/// <summary>校验 API Key 散列并以启动期材料签发 JWT</summary>
public sealed class JwtTokenIssuer
{
    public const string ClientIdClaim = "client_id";

    public const int MaxApiKeyHeaderLength = 512;

    private readonly IOptionsMonitor<AuthOptions> _options;
    private readonly JwtSigningMaterial _signing;

    public JwtTokenIssuer(IOptionsMonitor<AuthOptions> options, JwtSigningMaterial signing)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _signing = signing ?? throw new ArgumentNullException(nameof(signing));
    }

    public bool TryMatchApiKey(string? presented, out string clientId, out IReadOnlyList<string> scopes)
    {
        clientId = string.Empty;
        scopes = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        var trimmed = presented.Trim();
        if (trimmed.Length > MaxApiKeyHeaderLength)
        {
            return false;
        }

        var presentedHash = ApiKeyHasher.Hash(trimmed);
        foreach (var (id, client) in _options.CurrentValue.Clients)
        {
            if (string.IsNullOrWhiteSpace(id) || client is null || !client.Enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(client.ApiKeyHash))
            {
                continue;
            }

            if (!ApiKeyHasher.FixedTimeEqualsHex(presentedHash, client.ApiKeyHash))
            {
                continue;
            }

            clientId = id.Trim();
            scopes = AuthPolicies.NormalizeScopes(client.Scopes);
            return true;
        }

        return false;
    }

    public string? ReadApiKey(HttpRequest request)
    {
        var headerName = _options.CurrentValue.HeaderName.Trim();
        if (request.Headers.TryGetValue(headerName, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            return raw.ToString().Trim();
        }

        return null;
    }

    public (string AccessToken, int ExpiresInSeconds) Issue(string clientId, IReadOnlyList<string> scopes)
    {
        var expiresMinutes = _signing.ExpiresMinutes;
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(expiresMinutes);
        var creds = new SigningCredentials(_signing.SecurityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId),
            new(ClientIdClaim, clientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        foreach (var scope in scopes)
        {
            claims.Add(new Claim(AuthPolicies.ScopeClaim, scope));
        }

        var token = new JwtSecurityToken(
            issuer: _signing.Issuer,
            audience: _signing.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return (accessToken, (int)TimeSpan.FromMinutes(expiresMinutes).TotalSeconds);
    }
}
