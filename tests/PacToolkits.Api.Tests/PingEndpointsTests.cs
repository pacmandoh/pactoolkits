using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PacToolkits.Api.Tests;

public sealed class PingEndpointsTests
{
    [Fact]
    public async Task Ping_rejects_anonymous()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ping_accepts_bearer_jwt()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("pong", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal(ApiFactory.TestClientId, doc.RootElement.GetProperty("clientId").GetString());
    }
}
