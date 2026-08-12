using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Diagnostics;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Configuration;

namespace PacToolkits.Desktop.Tests;

/// <summary>覆盖 DI 注册的 JWT、Trace 与仅 GET Resilience（非内嵌 NestedJwtHandler）</summary>
[Collection(nameof(PacActivitiesCollection))]
public sealed class PacApiFactoryIntegrationTests
{
    [Fact]
    public async Task Factory_api_client_retries_get_and_injects_client_span_traceparent()
    {
        PacActivities.EnsureListening();
        var apiHits = 0;
        string? seenTraceParent = null;
        Activity? page = null;
        Activity? clientSpan = null;

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == PacActivities.DesktopName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _)
                => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                if (page is not null
                    && activity.OperationName == "pacapi.http"
                    && activity.TraceId == page.TraceId)
                {
                    clientSpan = activity;
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var services = new ServiceCollection();
        services.AddSingleton<IAppConfigStore>(new StubPacApiConfigStore());
        services.AddSingleton<IAppLogger, NullLogger>();
        services.AddPacApiClient();

        services.AddHttpClient(PacApiClient.TokenClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new FixedHandler(
                HttpStatusCode.OK,
                """{"accessToken":"tok","tokenType":"Bearer","expiresIn":3600,"clientId":"c"}"""));

        services.AddHttpClient(PacApiClient.ApiClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new CapturingHandler(req =>
            {
                Interlocked.Increment(ref apiHits);
                if (req.Headers.TryGetValues(PacTrace.HeaderName, out var values))
                {
                    seenTraceParent = values.FirstOrDefault();
                }

                return apiHits < 3
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json"),
                    };
            }));

        await using var sp = services.BuildServiceProvider();
        var api = sp.GetRequiredService<PacApiClient>();

        using (page = PacActivities.Desktop.StartActivity("factory.probe"))
        {
            Assert.NotNull(page);
            using var response = await api.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, api.Resolve("/health")),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(3, apiHits);
            Assert.NotNull(clientSpan);
            Assert.Equal(page!.TraceId, clientSpan!.TraceId);
            Assert.Equal(page.SpanId, clientSpan.ParentSpanId);
            Assert.Equal(clientSpan.Id, seenTraceParent);
            Assert.NotEqual(page.Id, seenTraceParent);
        }
    }

    private sealed class FixedHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> next) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(next(request));
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
                BaseUrl = "http://127.0.0.1:9",
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
