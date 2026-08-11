using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Desktop.Avalonia.Services.Integration;
using Polly;

namespace PacToolkits.Desktop.Tests;

public sealed class MsfxApiResilienceTests
{
    [Fact]
    public async Task Idempotent_post_retries_on_503()
    {
        var attempts = 0;
        var handler = new StubHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        var services = new ServiceCollection();
        services.AddHttpClient("msfx-probe")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("msfx", static builder =>
            {
                builder.AddRetry(MsfxApiRegistration.CreateTransportRetryOptions());
                builder.AddTimeout(MsfxApiRegistration.CreateAttemptTimeoutOptions());
            });

        await using var sp = services.BuildServiceProvider();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("msfx-probe");
        using var req = new HttpRequestMessage(HttpMethod.Post, "http://msfx.test/");
        req.Options.Set(MsfxApiClient.IdempotentKey, true);

        using var resp = await http.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task Non_idempotent_post_does_not_retry()
    {
        var attempts = 0;
        var handler = new StubHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var services = new ServiceCollection();
        services.AddHttpClient("msfx-probe-write")
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddResilienceHandler("msfx", static builder =>
            {
                builder.AddRetry(MsfxApiRegistration.CreateTransportRetryOptions());
            });

        await using var sp = services.BuildServiceProvider();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("msfx-probe-write");
        using var req = new HttpRequestMessage(HttpMethod.Post, "http://msfx.test/");

        using var resp = await http.SendAsync(req, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Query_methods_are_idempotent_allowlisted()
    {
        Assert.True(MsfxApiClient.IsIdempotentMethod(
            "alibaba.alihealth.drugtrace.top.yljg.listupout"));
        Assert.False(MsfxApiClient.IsIdempotentMethod("alibaba.alihealth.some.write"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }
}
