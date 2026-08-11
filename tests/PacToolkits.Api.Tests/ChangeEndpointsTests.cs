using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Api.Changes;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Tests;

public sealed class ChangeEndpointsTests
{
    [Fact]
    public async Task Watermarks_rejects_anonymous()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/changes/watermarks", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Watermarks_returns_items()
    {
        await using var factory = new ApiFactory();
        factory.Watermarks.Set(new ChangeWatermarkItem("inventory", 7));
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/changes/watermarks", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("inventory", body, StringComparison.Ordinal);
        Assert.Contains("7", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stream_emits_ready_and_change_events()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/changes/stream");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var bus = factory.Services.GetRequiredService<ChangeBus>();
        var buffer = new StringBuilder();

        // 先读到 ready，再 Publish，再等 change
        while (buffer.ToString().IndexOf("event: ready", StringComparison.Ordinal) < 0)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            Assert.NotNull(line);
            buffer.AppendLine(line);
        }

        bus.Publish("inventory");

        while (buffer.ToString().IndexOf("inventory", StringComparison.Ordinal) < 0)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            Assert.NotNull(line);
            buffer.AppendLine(line);
        }

        Assert.Contains("event: change", buffer.ToString(), StringComparison.Ordinal);
        Assert.Contains("inventory", buffer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stream_rejects_third_subscription_with_429()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        using var first = await OpenStreamAsync(client, cts.Token);
        using var second = await OpenStreamAsync(client, cts.Token);
        using var third = await OpenStreamAsync(client, cts.Token);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);
        Assert.Equal(TimeSpan.FromSeconds(5), third.Headers.RetryAfter.Delta);
    }

    [Fact]
    public async Task Stream_rejects_jwt_expired_within_clock_skew()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        // 过期约 30s：仍在 Bearer ClockSkew(1m) 内可通过认证，但 SSE 按绝对 exp 应 401
        var token = ApiFactory.ForgeAccessToken(expires: DateTime.UtcNow.AddSeconds(-30));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/v1/changes/stream", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Stream_ends_when_jwt_expires()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        // 短寿命 JWT：过 exp 后服务端应关掉 SSE
        var token = ApiFactory.ForgeAccessToken(expires: DateTime.UtcNow.AddSeconds(2));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var outer = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        outer.CancelAfter(TimeSpan.FromSeconds(15));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/changes/stream");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            outer.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(outer.Token);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var buffer = new StringBuilder();
        while (buffer.ToString().IndexOf("event: ready", StringComparison.Ordinal) < 0)
        {
            var line = await reader.ReadLineAsync(outer.Token);
            Assert.NotNull(line);
            buffer.AppendLine(line);
        }

        var started = Environment.TickCount64;
        while (!outer.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(outer.Token);
            if (line is null)
            {
                break;
            }

            buffer.AppendLine(line);
        }

        var elapsedMs = Environment.TickCount64 - started;
        Assert.True(
            elapsedMs < 10_000,
            $"SSE should end near JWT exp, elapsed={elapsedMs}ms");
        Assert.Contains("event: ready", buffer.ToString(), StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> OpenStreamAsync(HttpClient client, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/changes/stream");
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
    }
}
