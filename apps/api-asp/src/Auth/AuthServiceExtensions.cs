using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PacToolkits.Api.Auth;

public static class AuthServiceExtensions
{
    public static IServiceCollection AddPacToolkitsAuth(this IServiceCollection services, IConfiguration config)
    {
        services
            .AddOptions<AuthOptions>()
            .Bind(config.GetSection(AuthOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.HeaderName),
                "Auth:HeaderName is required")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Jwt.Issuer),
                "Auth:Jwt:Issuer is required")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Jwt.Audience),
                "Auth:Jwt:Audience is required")
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.Jwt.SigningKey) && o.Jwt.SigningKey.Trim().Length >= 32,
                "Auth:Jwt:SigningKey is required and must be at least 32 characters")
            .Validate(
                o => o.Jwt.ExpiresMinutes > 0,
                "Auth:Jwt:ExpiresMinutes must be greater than 0")
            .ValidateOnStart();

        services.AddSingleton<JwtTokenIssuer>();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, JwtBearerFromAuthOptions>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddAuthorization();
        return services;
    }
}

/// <summary>
/// JWT 校验参数与 AuthOptions 同源，避免注册期快照与配置覆盖脱节
/// </summary>
internal sealed class JwtBearerFromAuthOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly IOptionsMonitor<AuthOptions> _auth;

    public JwtBearerFromAuthOptions(IOptionsMonitor<AuthOptions> auth)
    {
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
    }

    public void Configure(JwtBearerOptions options)
        => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name is not null && name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        var jwt = _auth.CurrentValue.Jwt;
        var signingKey = jwt.SigningKey.Trim();
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    }
}
