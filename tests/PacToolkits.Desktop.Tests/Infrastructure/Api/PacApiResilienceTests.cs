using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;

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

    [Fact]
    public async Task SendAvailability_bypasses_open_circuit()
    {
        var apiHits = 0;
        var availabilityHits = 0;
        var services = new ServiceCollection();
        services.AddSingleton<IAppConfigStore>(new StubPacApiConfigStore());
        services.AddSingleton<IAppLogger, NullLogger>();
        services.AddPacApiClient();

        services.AddHttpClient(PacApiClient.TokenClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new FixedOkTokenHandler());

        // 业务客户端：连续 503 打开熔断
        services.AddHttpClient(PacApiClient.ApiClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new ScriptedStatusHandler(
                () =>
                {
                    Interlocked.Increment(ref apiHits);
                    return HttpStatusCode.ServiceUnavailable;
                }));

        services.AddHttpClient(PacApiClient.AvailabilityClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new ScriptedStatusHandler(
                () =>
                {
                    Interlocked.Increment(ref availabilityHits);
                    return HttpStatusCode.OK;
                }));

        await using var sp = services.BuildServiceProvider();
        var api = sp.GetRequiredService<PacApiClient>();

        // 凑满采样窗，打开熔断（MinimumThroughput=5，FailureRatio=0.5）
        for (var i = 0; i < 8; i++)
        {
            try
            {
                using var _ = await api.SendAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, api.Resolve("/v1/business")),
                    TestContext.Current.CancellationToken);
            }
            catch (PacApiException)
            {
                // 熔断打开后会包成 transport 503
            }
        }

        Assert.True(apiHits >= 5);

        using var probe = await api.SendAvailabilityAsync(
            () => new HttpRequestMessage(HttpMethod.Get, api.Resolve("/v1/system/status")),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        Assert.Equal(1, availabilityHits);
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

    private sealed class FixedOkTokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"accessToken":"tok","tokenType":"Bearer","expiresIn":3600,"clientId":"c"}""",
                    Encoding.UTF8,
                    "application/json"),
            });
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => string.Empty;
        public string CurrentLogPath => string.Empty;

        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null)
        {
        }

        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null)
        {
        }

        public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
        {
        }

        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
        {
        }

        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
        {
        }

        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class StubPacApiConfigStore : IAppConfigStore
    {
        public string ConfigPath => string.Empty;

        public AppConfigRoot Load() => new()
        {
            PacApi = new PacApiOptions
            {
                BaseUrl = "http://127.0.0.1:5080",
                ApiKey = "test-key",
            },
        };

        public void Save(AppConfigRoot config)
        {
        }

        public Task SaveAsync(AppConfigRoot config, CancellationToken ct = default)
            => Task.CompletedTask;

        public void Update(Action<AppConfigRoot> mutator)
        {
        }

        public Task UpdateAsync(Action<AppConfigRoot> mutator, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
