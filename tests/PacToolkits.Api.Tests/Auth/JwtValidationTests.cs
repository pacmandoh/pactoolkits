using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;

namespace PacToolkits.Api.Tests;

public sealed class JwtValidationTests
{
    [Fact]
    public async Task Ping_rejects_expired_token()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ApiFactory.ForgeAccessToken(expires: DateTime.UtcNow.AddMinutes(-10));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_wrong_issuer()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ApiFactory.ForgeAccessToken(issuer: "wrong-issuer");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_wrong_audience()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ApiFactory.ForgeAccessToken(audience: "wrong-audience");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_wrong_signing_key()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ApiFactory.ForgeAccessToken(signingKey: "other-jwt-signing-key-32chars-min!!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_missing_client_id_claim()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ApiFactory.ForgeAccessToken(includeClientId: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_rejects_token_without_read_scope()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ApiFactory.ForgeAccessToken(scopes: [AuthPolicies.Write]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SystemInfo_requires_system_status_scope()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = ApiFactory.ForgeAccessToken(scopes: [AuthPolicies.Read]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/system/info", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SystemInfo_returns_contract_fields()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/system/info", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(ApiContract.Version, doc.RootElement.GetProperty("contractVersion").GetString());
        Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("apiVersion").GetString()));
    }
}
