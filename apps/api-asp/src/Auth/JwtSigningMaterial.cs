using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PacToolkits.Api.Auth;

/// <summary>
/// 进程启动时固定的 JWT 签发/校验材料
///
/// SigningKey 变更须重启；不支持运行中轮换
/// </summary>
public sealed class JwtSigningMaterial
{
    public JwtSigningMaterial(JwtOptions jwt)
    {
        ArgumentNullException.ThrowIfNull(jwt);
        var signingKey = jwt.SigningKey.Trim();
        if (signingKey.Length < 32)
        {
            throw new ArgumentException("Auth:Jwt:SigningKey must be at least 32 characters", nameof(jwt));
        }

        Issuer = jwt.Issuer.Trim();
        Audience = jwt.Audience.Trim();
        ExpiresMinutes = jwt.ExpiresMinutes;
        SecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }

    public string Issuer { get; }

    public string Audience { get; }

    public int ExpiresMinutes { get; }

    public SymmetricSecurityKey SecurityKey { get; }
}
