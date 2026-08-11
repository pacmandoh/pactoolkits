using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Tests;

public sealed class PacApiEnsureSuccessTests
{
    [Fact]
    public async Task EnsureSuccessAsync_parses_problem_details()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"title":"unavailable","status":503,"code":"service_unavailable","traceId":"deadbeef"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => PacApiClient.EnsureSuccessAsync(response, TimeProvider.System, TestContext.Current.CancellationToken));

        Assert.Equal(503, ex.Status);
        Assert.Equal("service_unavailable", ex.Code);
        Assert.Equal("deadbeef", ex.TraceId);
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task EnsureSuccessAsync_noop_on_2xx()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        await PacApiClient.EnsureSuccessAsync(response, TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RetryAfter_date_uses_injected_TimeProvider()
    {
        var time = new ControllableTime(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(
                """{"title":"rate","status":429,"code":"rate_limited"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(time.GetUtcNow().AddSeconds(30));

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => PacApiClient.EnsureSuccessAsync(response, time, TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
    }

    [Fact]
    public async Task RetryAfter_delta_is_parsed()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"title":"busy","status":503,"code":"service_unavailable"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => PacApiClient.EnsureSuccessAsync(response, TimeProvider.System, TestContext.Current.CancellationToken));

        Assert.Equal(TimeSpan.FromSeconds(12), ex.RetryAfter);
        Assert.Equal(503, ex.Status);
    }

    [Fact]
    public async Task Body_status_does_not_override_http_status()
    {
        var log = new CaptureLogger();
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent(
                """{"title":"mismatch","status":400,"code":"bad_request"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => PacApiClient.EnsureSuccessAsync(
                response,
                TimeProvider.System,
                log,
                TestContext.Current.CancellationToken));

        Assert.Equal(503, ex.Status);
        Assert.True(ex.IsTransient);
        Assert.Contains(log.Events, e => e.Contains("problem.status_mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Http_400_stays_non_transient_when_body_says_503()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"title":"mismatch","status":503,"code":"service_unavailable"}""",
                Encoding.UTF8,
                "application/problem+json"),
        };

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => PacApiClient.EnsureSuccessAsync(response, TimeProvider.System, TestContext.Current.CancellationToken));

        Assert.Equal(400, ex.Status);
        Assert.False(ex.IsTransient);
    }

    private sealed class CaptureLogger : IAppLogger
    {
        public List<string> Events { get; } = [];

        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/pac-api-ensure-success-test.log";

        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null) { }

        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null) { }

        public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
            => Events.Add($"{eventName}:{message}");

        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }

        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }

        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
