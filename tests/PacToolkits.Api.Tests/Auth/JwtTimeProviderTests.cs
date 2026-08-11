using System.Net;
using System.Net.Http.Headers;

namespace PacToolkits.Api.Tests;

public sealed class JwtTimeProviderTests
{
    [Fact]
    public async Task Issue_and_auth_follow_injected_TimeProvider()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        await using var factory = new ApiFactory { Time = time };
        using var client = factory.CreateClient();

        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using (var ok = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        // nbf=签发时刻；拨回超过 ClockSkew(1m) 应尚未生效
        time.Utc = time.Utc.AddMinutes(-2);
        using (var notYet = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, notYet.StatusCode);
        }

        // 回到签发后仍在 TTL 内
        time.Utc = new DateTimeOffset(2026, 6, 1, 12, 5, 0, TimeSpan.Zero);
        using (var stillOk = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, stillOk.StatusCode);
        }

        // ExpiresMinutes=30，加 ClockSkew 1m；拨到 32m 后过期
        time.Utc = new DateTimeOffset(2026, 6, 1, 12, 32, 0, TimeSpan.Zero);
        using (var expired = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        public ManualTime(DateTimeOffset utc) => Utc = utc;

        public DateTimeOffset Utc { get; set; }

        public override DateTimeOffset GetUtcNow() => Utc;
    }
}
