using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PacToolkits.Api.Auth;

/// <summary>校验 API Key 并用对称密钥签发 JWT</summary>
public sealed class JwtTokenIssuer
{
    public const string ClientIdClaim = "client_id";

    private readonly IOptionsMonitor<AuthOptions> _options;

    public JwtTokenIssuer(IOptionsMonitor<AuthOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool TryMatchApiKey(string? presented, out string clientId)
    {
        clientId = string.Empty;
        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        var presentedBytes = Encoding.UTF8.GetBytes(presented.Trim());
        var keys = _options.CurrentValue.ApiKeys;
        for (var i = 0; i < keys.Count; i++)
        {
            var candidate = keys[i];
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var candidateBytes = Encoding.UTF8.GetBytes(candidate.Trim());
            if (presentedBytes.Length == candidateBytes.Length
                && CryptographicOperations.FixedTimeEquals(presentedBytes, candidateBytes))
            {
                clientId = $"site-{i}";
                return true;
            }
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

    public (string AccessToken, int ExpiresInSeconds) Issue(string clientId)
    {
        var jwt = _options.CurrentValue.Jwt;
        var expiresMinutes = jwt.ExpiresMinutes;
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(expiresMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey.Trim()));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, clientId),
            new Claim(ClientIdClaim, clientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return (accessToken, (int)TimeSpan.FromMinutes(expiresMinutes).TotalSeconds);
    }
}
