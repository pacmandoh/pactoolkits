using System.Text.Json;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Desktop.Tests;

public sealed class PacJsonContextTests
{
    [Fact]
    public void ChangeWatermarksResponse_roundtrips_camelCase()
    {
        const string json = """{"items":[{"topic":"inventory","version":7}]}""";
        var body = JsonSerializer.Deserialize(json, PacJsonContext.Default.ChangeWatermarksResponse);
        Assert.NotNull(body);
        var item = Assert.Single(body.Items);
        Assert.Equal("inventory", item.Topic);
        Assert.Equal(7, item.Version);

        var again = JsonSerializer.Serialize(body, PacJsonContext.Default.ChangeWatermarksResponse);
        Assert.Contains("inventory", again, StringComparison.Ordinal);
        Assert.Contains("7", again, StringComparison.Ordinal);
    }

    [Fact]
    public void PacApiTokenResponse_deserializes()
    {
        const string json =
            """{"accessToken":"tok","tokenType":"Bearer","expiresIn":3600,"clientId":"c1"}""";
        var body = JsonSerializer.Deserialize(json, PacJsonContext.Default.PacApiTokenResponse);
        Assert.NotNull(body);
        Assert.Equal("tok", body.AccessToken);
        Assert.Equal(3600, body.ExpiresIn);
    }
}
