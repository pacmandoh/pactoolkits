using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class PacApiResilienceTests
{
    [Fact]
    public async Task Get_retry_pipeline_retries_transient_5xx()
    {
        var hits = 0;
        var services = new ServiceCollection();
        services.AddHttpClient("probe-get")
            .ConfigurePrimaryHttpMessageHandler(() => new ScriptedStatusHandler(
                () =>
                {
                    Interlocked.Increment(ref hits);
                    return hits < 3
                        ? HttpStatusCode.ServiceUnavailable
                        : HttpStatusCode.OK;
                }))
            .AddResilienceHandler("get", static builder => PacApiRegistration.ConfigureGetOnlyPipeline(builder));

        await using var sp = services.BuildServiceProvider();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("probe-get");
        using var response = await http.GetAsync("http://127.0.0.1/v1/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, hits);
    }

    [Fact]
    public async Task Get_retry_pipeline_does_not_retry_post()
    {
        var hits = 0;
        var services = new ServiceCollection();
        services.AddHttpClient("probe-post")
            .ConfigurePrimaryHttpMessageHandler(() => new ScriptedStatusHandler(
                () =>
                {
                    Interlocked.Increment(ref hits);
                    return HttpStatusCode.ServiceUnavailable;
                }))
            .AddResilienceHandler("get", static builder => PacApiRegistration.ConfigureGetOnlyPipeline(builder));

        await using var sp = services.BuildServiceProvider();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("probe-post");
        using var response = await http.PostAsync(
            "http://127.0.0.1/v1/auth/token",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, hits);
    }

    [Fact]
    public async Task Get_attempt_timeout_does_not_cut_slow_post()
    {
        var delay = TimeSpan.FromMilliseconds(250);
        var getAttemptTimeout = TimeSpan.FromMilliseconds(50);
        var services = new ServiceCollection();
        services.AddHttpClient("probe-slow-post")
            .ConfigureHttpClient(static client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new DelayedOkHandler(delay))
            .AddResilienceHandler(
                "get",
                builder => PacApiRegistration.ConfigureGetOnlyPipeline(builder, getAttemptTimeout));

        await using var sp = services.BuildServiceProvider();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("probe-slow-post");
        var sw = Stopwatch.StartNew();
        using var response = await http.PostAsync(
            "http://127.0.0.1/v1/commands",
            content: null,
            TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(
            sw.Elapsed >= delay,
            $"POST should wait out handler delay ({delay.TotalMilliseconds}ms), elapsed={sw.ElapsedMilliseconds}ms");
    }

    private sealed class ScriptedStatusHandler(Func<HttpStatusCode> next) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(next()));
    }

    private sealed class DelayedOkHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
