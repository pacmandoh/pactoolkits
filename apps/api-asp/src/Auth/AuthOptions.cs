namespace PacToolkits.Api.Auth;

/// <summary>
/// 换票用站点 API Key 与 JWT 签发参数
///
/// 受保护路由验 JWT；Key 仅用于 POST /v1/auth/token
/// 生产：Auth:ApiKeys / Auth:Jwt:SigningKey 走环境变量；SigningKey ≥32 字符，勿入库真密钥
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public const string DefaultHeaderName = "X-Api-Key";

    public string HeaderName { get; set; } = DefaultHeaderName;

    public List<string> ApiKeys { get; set; } = [];

    public JwtOptions Jwt { get; set; } = new();
}

/// <summary>HMAC 对称签 JWT 的 Issuer/Audience/SigningKey/TTL</summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "pactoolkits-api";

    public string Audience { get; set; } = "pactoolkits-clients";

    public string SigningKey { get; set; } = string.Empty;

    public int ExpiresMinutes { get; set; } = 60;
}
