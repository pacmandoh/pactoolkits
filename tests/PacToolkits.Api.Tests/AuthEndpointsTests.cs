using System.Net;
using System.Text.Json;
using PacToolkits.Api.Auth;

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
}
