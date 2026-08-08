using System.Net;
using System.Text.Json;

namespace PacToolkits.Api.Tests;

public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task Health_allows_anonymous_when_probe_ok()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("ok", doc.RootElement.GetProperty("database").GetString());
        Assert.Equal("ok", doc.RootElement.GetProperty("schema").GetString());
        Assert.True(doc.RootElement.TryGetProperty("utc", out _));
    }

    [Fact]
    public async Task Health_returns_503_when_database_unreachable()
    {
        await using var factory = new RealHealthApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("unavailable", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("unavailable", doc.RootElement.GetProperty("database").GetString());
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
    }
}
