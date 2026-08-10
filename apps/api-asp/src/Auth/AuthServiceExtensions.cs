using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PacToolkits.Api.Auth;

public static class AuthServiceExtensions
{
    public const string TokenRateLimitPolicy = "auth-token";

    public static IServiceCollection AddPacToolkitsAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
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

        services.AddSingleton(sp =>
        {
            var auth = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
            return new JwtSigningMaterial(auth.Jwt);
        });

        services.AddSingleton<JwtTokenIssuer>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, JwtBearerFromSigningMaterial>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                AuthPolicies.Read,
                p => p.RequireAuthenticatedUser().RequireClaim(AuthPolicies.ScopeClaim, AuthPolicies.Read));
            options.AddPolicy(
                AuthPolicies.Write,
                p => p.RequireAuthenticatedUser().RequireClaim(AuthPolicies.ScopeClaim, AuthPolicies.Write));
            options.AddPolicy(
                AuthPolicies.SystemStatus,
                p => p.RequireAuthenticatedUser()
                    .RequireClaim(AuthPolicies.ScopeClaim, AuthPolicies.SystemStatus));
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // 按 RemoteIp：同一机器转发或 NAT 会共用额度；伪造 XFF 依赖代理覆盖与 KnownProxies
            options.AddPolicy(TokenRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}

/// <summary>JWT 校验使用进程启动时固定的 SigningMaterial（不随热更配置重载）</summary>
internal sealed class JwtBearerFromSigningMaterial : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly JwtSigningMaterial _signing;

    public JwtBearerFromSigningMaterial(JwtSigningMaterial signing)
    {
        _signing = signing ?? throw new ArgumentNullException(nameof(signing));
    }

    public void Configure(JwtBearerOptions options)
        => Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name is not null && name != JwtBearerDefaults.AuthenticationScheme)
        {
            return;
        }

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _signing.Issuer,
            ValidateAudience = true,
            ValidAudience = _signing.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signing.SecurityKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        };
    }
}
