namespace PacToolkits.Api.Auth;

/// <summary>
/// 换票用站点客户端与 JWT 签发参数
///
/// 受保护路由验 JWT；明文 API Key 仅用于 POST /v1/auth/token
/// Clients 键为稳定 client id；服务端只存 ApiKeyHash；SigningKey 变更后须重启进程
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public const string DefaultHeaderName = "X-Api-Key";

    public string HeaderName { get; set; } = DefaultHeaderName;

    public Dictionary<string, ClientOptions> Clients { get; set; } =
        new(StringComparer.Ordinal);

    public JwtOptions Jwt { get; set; } = new();
}

/// <summary>具名客户端：ApiKeyHash（SHA-256 hex）、Enabled、Scopes</summary>
public sealed class ClientOptions
{
    public string ApiKeyHash { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public List<string> Scopes { get; set; } = [];
}

/// <summary>HMAC 对称签 JWT 的 Issuer/Audience/SigningKey/TTL</summary>
public sealed class JwtOptions
{
    public const int MinExpiresMinutes = 1;

    public const int MaxExpiresMinutes = 24 * 60;

    public string Issuer { get; set; } = "pactoolkits-api";

    public string Audience { get; set; } = "pactoolkits-clients";

    public string SigningKey { get; set; } = string.Empty;

    public int ExpiresMinutes { get; set; } = 60;
}
