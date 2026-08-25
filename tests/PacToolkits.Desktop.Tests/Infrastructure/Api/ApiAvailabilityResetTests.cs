using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Versioning;

namespace PacToolkits.Desktop.Tests;

public sealed class ApiAvailabilityResetTests
{
    [Fact]
    public async Task Reset_discards_probe_started_before_configuration_change()
    {
        var logger = new NullLogger();
        var probe = new ProbeHandler();
        using var api = new PacApiClient(
            "http://127.0.0.1:5000",
            "old-key",
            logger,
            "X-Api-Key",
            new FixedHandler(TokenJson),
            probe,
            new FixedHandler("{}"));
        using var availability = new ApiAvailabilityService(
            api,
            new StubVersions(),
            logger,
            TimeProvider.System);

        await availability.ProbeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ApiAvailabilityState.Ready, availability.Current.State);

        var staleProbe = availability.ProbeAsync(TestContext.Current.CancellationToken);
        await probe.SecondStatusStarted.Task.WaitAsync(TestContext.Current.CancellationToken);

        api.Apply(new PacApiOptions
        {
            BaseUrl = "http://127.0.0.1:5001",
            ApiKey = "new-key",
        });
        availability.Reset();
        probe.ReleaseSecondStatus.SetResult();
        await staleProbe;

        Assert.Equal(ApiAvailabilityState.Connecting, availability.Current.State);
        Assert.False(availability.Current.FirstCheckCompleted);
        Assert.Null(availability.LastDatabase);
    }

    private const string TokenJson =
        """{"accessToken":"tok","tokenType":"Bearer","expiresIn":3600,"clientId":"c"}""";

    private sealed class ProbeHandler : HttpMessageHandler
    {
        private int _statusHits;

        public TaskCompletionSource SecondStatusStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseSecondStatus { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/v1/system/info", StringComparison.Ordinal) == true)
            {
                return Json(
                    """{"product":"PacToolkits.Api","apiVersion":"1.0.0","contractVersion":"1.0.0","utc":"2026-01-01T00:00:00Z"}""");
            }

            if (Interlocked.Increment(ref _statusHits) == 2)
            {
                SecondStatusStarted.SetResult();
                await ReleaseSecondStatus.Task.WaitAsync(cancellationToken);
            }

            return Json(
                """{"status":"ok","utc":"2026-01-01T00:00:00Z","database":"ok","schema":"ok","schemaVersion":"1.0.0","reason":null}""");
        }

        private static HttpResponseMessage Json(string body)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
    }

    private sealed class FixedHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StubVersions : IReleaseVersionService
    {
        public ReleaseVersionInfo Current { get; } = new(
            ProductVersion: "1.0.0",
            DesktopVersion: "1.0.0",
            AgentsVersion: "1.0.0",
            DbSchemaVersion: "1.0.0",
            BuildChannel: "test",
            BuildDate: "2026-01-01",
            MinApiContract: "1.0.0",
            MaxApiContract: "1.0.0");
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => string.Empty;
        public string CurrentLogPath => string.Empty;

        public void Debug(
            string module,
            string eventName,
            string message,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Info(
            string module,
            string eventName,
            string message,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Warn(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Error(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public void Fatal(
            string module,
            string eventName,
            string message,
            Exception? ex = null,
            object? context = null,
            string? traceId = null)
        {
        }

        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
