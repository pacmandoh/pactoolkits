using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            ["SchemaBounds:MinDbSchema"] = "1.2.25",
            ["SchemaBounds:MaxDbSchema"] = "1.2.25",
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

    private sealed class AlwaysOkApiHealth : IApiHealth
    {
        public Task<ApiHealthSnapshot> CheckAsync(CancellationToken ct = default)
            => Task.FromResult(new ApiHealthSnapshot(Ok: true, Database: "ok", Schema: "ok", SchemaVersion: "1.2.25"));
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
