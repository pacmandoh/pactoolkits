using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

/// <summary>内存宿主：具名客户端与固定 JWT 材料；默认健康检查始终成功；LISTEN 关闭</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestClientId = "test-client";

    public const string TestApiKey = "test-api-key-plaintext";

    public const string TestJwtSigningKey = "test-jwt-signing-key-32chars-min!!";

    public static string TestApiKeyHash { get; } = ApiKeyHasher.Hash(TestApiKey);

    public FakeChangeWatermarkRepo Watermarks { get; } = new();

    /// <summary>非空时替换宿主 TimeProvider（签发与 JWT 寿命校验共用）</summary>
    public TimeProvider? Time { get; init; }

    /// <summary>非空时替换 <see cref="IDashboardService"/></summary>
    public IDashboardService? Dashboard { get; init; }

    /// <summary>非空时替换 <see cref="ILookupCatalogService"/></summary>
    public ILookupCatalogService? Lookup { get; init; }

    /// <summary>非空时替换 <see cref="IDrugIndexService"/></summary>
    public IDrugIndexService? Drugs { get; init; }

    /// <summary>非空时替换 <see cref="IScanCodeService"/></summary>
    public IScanCodeService? ScanCode { get; init; }

    /// <summary>非空时替换 <see cref="IInventoryOverviewService"/></summary>
    public IInventoryOverviewService? Inventory { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("dev");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(BuildAuthConfig());
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IApiHealth, AlwaysOkApiHealth>();
            services.AddSingleton<IChangeWatermarkRepo>(Watermarks);
            // 写命令测试：内存 dedup，回调 IDb（不连 PostgreSQL）
            services.Replace(ServiceDescriptor.Singleton<ICommandDedup, MemoryCommandDedup>());
            services.Replace(ServiceDescriptor.Singleton<IDb, CallbackDb>());
            if (Time is not null)
            {
                services.AddSingleton(Time);
            }

            if (Dashboard is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<IDashboardService>(Dashboard));
            }

            if (Lookup is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<ILookupCatalogService>(Lookup));
            }

            if (Drugs is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<IDrugIndexService>(Drugs));
            }

            if (ScanCode is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<IScanCodeService>(ScanCode));
            }

            if (Inventory is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<IInventoryOverviewService>(Inventory));
            }
        });
    }

    public static Dictionary<string, string?> BuildAuthConfig()
        => new()
        {
            [$"Auth:Clients:{TestClientId}:ApiKeyHash"] = TestApiKeyHash,
            [$"Auth:Clients:{TestClientId}:Enabled"] = "true",
            [$"Auth:Clients:{TestClientId}:Scopes:0"] = AuthPolicies.Read,
            [$"Auth:Clients:{TestClientId}:Scopes:1"] = AuthPolicies.Write,
            [$"Auth:Clients:{TestClientId}:Scopes:2"] = AuthPolicies.SystemStatus,
            ["Auth:HeaderName"] = AuthOptions.DefaultHeaderName,
            ["Auth:Jwt:Issuer"] = "pactoolkits-api-test",
            ["Auth:Jwt:Audience"] = "pactoolkits-clients-test",
            ["Auth:Jwt:SigningKey"] = TestJwtSigningKey,
            ["Auth:Jwt:ExpiresMinutes"] = "30",
            ["SchemaBounds:MinDbSchema"] = "1.2.26",
            ["SchemaBounds:MaxDbSchema"] = "1.2.26",
            ["Changes:ListenEnabled"] = "false",
            ["Postgres:Host"] = "127.0.0.1",
            ["Postgres:Port"] = "1",
            ["Postgres:Database"] = "postgres",
            ["Postgres:Username"] = "postgres",
            ["Postgres:Password"] = "unused",
            ["Postgres:ConnectTimeoutSeconds"] = "1",
        };

    public static async Task<string> FetchAccessTokenAsync(
        HttpClient client,
        CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/token");
        request.Headers.Add(AuthOptions.DefaultHeaderName, TestApiKey);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("token response missing accessToken");
    }

    public static string ForgeAccessToken(
        DateTime? expires = null,
        string issuer = "pactoolkits-api-test",
        string audience = "pactoolkits-clients-test",
        string signingKey = TestJwtSigningKey,
        bool includeClientId = true,
        IReadOnlyList<string>? scopes = null)
    {
        var now = DateTime.UtcNow;
        var expiresAt = expires ?? now.AddMinutes(30);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, TestClientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        if (includeClientId)
        {
            claims.Add(new Claim(JwtTokenIssuer.ClientIdClaim, TestClientId));
        }

        foreach (var scope in scopes ?? [AuthPolicies.Read, AuthPolicies.Write, AuthPolicies.SystemStatus])
        {
            claims.Add(new Claim(AuthPolicies.ScopeClaim, scope));
        }

        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: expiresAt.AddMinutes(-60),
            expires: expiresAt,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private sealed class AlwaysOkApiHealth : IApiHealth
    {
        public Task<ApiHealthSnapshot> CheckAsync(CancellationToken ct = default)
            => Task.FromResult(new ApiHealthSnapshot(Ok: true, Database: "ok", Schema: "ok", SchemaVersion: "1.2.26"));
    }

    /// <summary>只跑回调，给 MemoryCommandDedup 写路径用；不连 PostgreSQL</summary>
    private sealed class CallbackDb : IDb
    {
        public Task<IAsyncDisposable?> TryAcquireSessionLockAsync(string key, CancellationToken ct = default)
            => Task.FromResult<IAsyncDisposable?>(null);

        public Task<T> WithConnection<T>(
            Func<IDbConnection, CancellationToken, Task<T>> work,
            CancellationToken ct = default)
            => work(StubConnection.Instance, ct);

        public Task WithConnection(
            Func<IDbConnection, CancellationToken, Task> work,
            CancellationToken ct = default)
            => work(StubConnection.Instance, ct);

        public Task<T> WithTransaction<T>(
            Func<IDbConnection, IDbTransaction, CancellationToken, Task<T>> work,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken ct = default)
            => work(StubConnection.Instance, StubTransaction.Instance, ct);

        public Task WithTransaction(
            Func<IDbConnection, IDbTransaction, CancellationToken, Task> work,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            CancellationToken ct = default)
            => work(StubConnection.Instance, StubTransaction.Instance, ct);
    }

    private sealed class StubConnection : IDbConnection
    {
        public static readonly StubConnection Instance = new();

        [AllowNull]
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "stub";
        public ConnectionState State => ConnectionState.Open;

        public IDbTransaction BeginTransaction() => StubTransaction.Instance;

        public IDbTransaction BeginTransaction(IsolationLevel il) => StubTransaction.Instance;

        public void ChangeDatabase(string databaseName)
        {
        }

        public void Close()
        {
        }

        public IDbCommand CreateCommand()
            => throw new NotSupportedException("CallbackDb does not execute SQL");

        public void Open()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class StubTransaction : IDbTransaction
    {
        public static readonly StubTransaction Instance = new();

        public IDbConnection? Connection => StubConnection.Instance;
        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public void Commit()
        {
        }

        public void Rollback()
        {
        }

        public void Dispose()
        {
        }
    }
}

public sealed class FakeChangeWatermarkRepo : IChangeWatermarkRepo
{
    private IReadOnlyList<ChangeWatermarkItem> _items = [];

    public void Set(params ChangeWatermarkItem[] items) => _items = items;

    public Task<IReadOnlyList<ChangeWatermarkItem>> ListAsync(CancellationToken ct = default)
        => Task.FromResult(_items);
}

/// <summary>使用真实 ApiHealth；默认指向不可达端口，期望 503</summary>
public sealed class RealHealthApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("dev");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(ApiFactory.BuildAuthConfig());
        });
    }
}
