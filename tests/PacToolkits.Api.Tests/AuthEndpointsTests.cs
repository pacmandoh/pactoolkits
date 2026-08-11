using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;

namespace PacToolkits.Api.Tests;

public sealed class AuthEndpointsTests
{
    [Fact]
    public async Task Token_rejects_missing_api_key()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/v1/auth/token", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_rejects_invalid_api_key()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/token");
        request.Headers.Add(AuthOptions.DefaultHeaderName, "wrong-key");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_rejects_oversized_api_key_header()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/token");
        request.Headers.Add(AuthOptions.DefaultHeaderName, new string('a', JwtTokenIssuer.MaxApiKeyHeaderLength + 1));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_returns_bearer_jwt_with_stable_client_id()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/token");
        request.Headers.Add(AuthOptions.DefaultHeaderName, ApiFactory.TestApiKey);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Bearer", doc.RootElement.GetProperty("tokenType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("accessToken").GetString()));
        Assert.True(doc.RootElement.GetProperty("expiresIn").GetInt32() > 0);
        Assert.Equal(ApiFactory.TestClientId, doc.RootElement.GetProperty("clientId").GetString());
    }

    [Fact]
    public async Task Token_rejects_disabled_client()
    {
        await using var factory = new DisabledClientFactory();
        using var client = factory.CreateClient();

        using var denied = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/token");
        denied.Headers.Add(AuthOptions.DefaultHeaderName, DisabledClientFactory.DisabledApiKey);
        using var deniedResponse = await client.SendAsync(denied, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, deniedResponse.StatusCode);

        using var ok = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/token");
        ok.Headers.Add(AuthOptions.DefaultHeaderName, ApiFactory.TestApiKey);
        using var okResponse = await client.SendAsync(ok, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
    }

    [Fact]
    public async Task Token_rate_limit_returns_429()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        HttpResponseMessage? limited = null;
        for (var i = 0; i < 40; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/token");
            request.Headers.Add(AuthOptions.DefaultHeaderName, "wrong-key");
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
                break;
            }

            response.Dispose();
        }

        Assert.NotNull(limited);
        using (limited)
        {
            Assert.NotNull(limited.Headers.RetryAfter);
            Assert.True(
                limited.Headers.RetryAfter.Delta is { } delta && delta > TimeSpan.Zero,
                "token rate limit 429 should carry Retry-After");
        }
    }

    [Fact]
    public void Host_startup_rejects_enabled_client_without_hash()
    {
        using var factory = new EmptyHashClientFactory();
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("ApiKeyHash", Flatten(ex), StringComparison.Ordinal);
    }

    private static string Flatten(Exception ex)
    {
        var parts = new List<string>();
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            parts.Add(cur.Message);
        }

        return string.Join(" | ", parts);
    }

    private sealed class DisabledClientFactory : WebApplicationFactory<Program>
    {
        public const string DisabledApiKey = "disabled-client-plaintext-key";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("dev");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var overrides = ApiFactory.BuildAuthConfig();
                overrides["Auth:Clients:retired:ApiKeyHash"] = ApiKeyHasher.Hash(DisabledApiKey);
                overrides["Auth:Clients:retired:Enabled"] = "false";
                overrides["Auth:Clients:retired:Scopes:0"] = AuthPolicies.Read;
                config.AddInMemoryCollection(overrides);
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IApiHealth>(_ => new FixedOkHealth());
            });
        }
    }

    private sealed class FixedOkHealth : IApiHealth
    {
        public Task<ApiHealthSnapshot> CheckAsync(CancellationToken ct = default)
            => Task.FromResult(new ApiHealthSnapshot(Ok: true, Database: "ok", Schema: "ok", SchemaVersion: "1.2.25"));
    }

    private sealed class EmptyHashClientFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("dev");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var overrides = ApiFactory.BuildAuthConfig();
                overrides[$"Auth:Clients:{ApiFactory.TestClientId}:ApiKeyHash"] = string.Empty;
                config.AddInMemoryCollection(overrides);
            });
        }
    }
}
