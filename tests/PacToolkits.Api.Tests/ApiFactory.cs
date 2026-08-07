using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using PacToolkits.Api.Auth;

namespace PacToolkits.Api.Tests;

/// <summary>内存宿主：注入固定 Auth 测试配置</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "test-api-key";

    public const string TestJwtSigningKey = "test-jwt-signing-key-32chars-min!!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("dev");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:ApiKeys:0"] = TestApiKey,
                ["Auth:HeaderName"] = AuthOptions.DefaultHeaderName,
                ["Auth:Jwt:Issuer"] = "pactoolkits-api-test",
                ["Auth:Jwt:Audience"] = "pactoolkits-clients-test",
                ["Auth:Jwt:SigningKey"] = TestJwtSigningKey,
                ["Auth:Jwt:ExpiresMinutes"] = "30",
            });
        });
    }

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
}
