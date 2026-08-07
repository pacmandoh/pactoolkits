using System.Net;
using System.Text.Json;

namespace PacToolkits.Api.Tests;

public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task Health_allows_anonymous()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.True(doc.RootElement.TryGetProperty("utc", out _));
    }
}
