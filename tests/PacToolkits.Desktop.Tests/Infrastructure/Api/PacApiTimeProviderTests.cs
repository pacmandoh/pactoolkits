using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

namespace PacToolkits.Desktop.Tests;

public sealed class PacApiTimeProviderTests
{
    [Fact]
    public async Task Token_expiry_follows_injected_TimeProvider()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var tokenCalls = 0;
        var tokenHandler = new ScriptHandler((req, _) =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/v1/auth/token", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref tokenCalls);
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"accessToken":"t","tokenType":"Bearer","expiresIn":120,"clientId":"c"}"""),
                });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            });
        });

        using var pac = new PacApiClient(
            "http://127.0.0.1:9",
            "key",
            new NullLogger(),
            "X-Api-Key",
            tokenHandler,
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK))),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK))),
            time);

        await pac.EnsureTokenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, tokenCalls);

        time.Utc = time.Utc.AddSeconds(30);
        await pac.EnsureTokenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, tokenCalls);

        time.Utc = time.Utc.AddSeconds(100);
        await pac.EnsureTokenAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, tokenCalls);
    }

    private sealed class ManualTime : TimeProvider
    {
        public ManualTime(DateTimeOffset utc) => Utc = utc;

        public DateTimeOffset Utc { get; set; }

        public override DateTimeOffset GetUtcNow() => Utc;
    }

    private sealed class ScriptHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;

        public ScriptHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn)
            => _fn = fn;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _fn(request, cancellationToken);
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
