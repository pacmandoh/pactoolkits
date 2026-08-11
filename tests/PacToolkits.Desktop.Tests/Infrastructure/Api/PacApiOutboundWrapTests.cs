using System.Net;
using System.Text;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Serialization;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Connectivity;

namespace PacToolkits.Desktop.Tests;

public sealed class PacApiOutboundWrapTests
{
    [Fact]
    public async Task SendAsync_wraps_http_request_exception_as_transient_pac_api()
    {
        using var pac = new PacApiClient(
            "http://127.0.0.1:9",
            "key",
            new NullLogger(),
            "X-Api-Key",
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"accessToken":"t","tokenType":"Bearer","expiresIn":3600,"clientId":"c"}"""),
            })),
            new ScriptHandler((_, _) => throw new HttpRequestException("Connection refused")),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => pac.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, pac.Resolve("/v1/x")),
                TestContext.Current.CancellationToken));

        Assert.Equal("transport", ex.Code);
        Assert.True(ex.IsTransient);
        Assert.True(TransportErrors.IsTransport(ex));
        Assert.False(TransportErrors.SignalsDbDisconnect(ex));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task GetJsonAsync_wraps_body_io_exception_as_transient_pac_api()
    {
        using var pac = new PacApiClient(
            "http://127.0.0.1:9",
            "key",
            new NullLogger(),
            "X-Api-Key",
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"accessToken":"t","tokenType":"Bearer","expiresIn":3600,"clientId":"c"}""",
                    Encoding.UTF8,
                    "application/json"),
            })),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ThrowingReadStream()),
            })),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => pac.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, pac.Resolve("/v1/changes/watermarks")),
                PacJsonContext.Default.ChangeWatermarksResponse,
                TestContext.Current.CancellationToken));

        Assert.Equal("transport", ex.Code);
        Assert.True(ex.IsTransient);
        Assert.False(TransportErrors.SignalsDbDisconnect(ex));
        Assert.IsType<IOException>(ex.InnerException);
    }

    [Fact]
    public async Task Token_response_body_io_exception_wraps_as_transient_pac_api()
    {
        using var pac = new PacApiClient(
            "http://127.0.0.1:9",
            "key",
            new NullLogger(),
            "X-Api-Key",
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new ThrowingReadStream()),
            })),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json"),
            })),
            new ScriptHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));

        var ex = await Assert.ThrowsAsync<PacApiException>(
            () => pac.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, pac.Resolve("/v1/x")),
                TestContext.Current.CancellationToken));

        Assert.Equal("transport", ex.Code);
        Assert.True(ex.IsTransient);
        Assert.False(TransportErrors.SignalsDbDisconnect(ex));
        // HttpClient 读正文时可能把 IOException 再包一层 HttpRequestException
        Assert.True(
            ex.InnerException is IOException or HttpRequestException,
            $"unexpected inner: {ex.InnerException?.GetType().FullName}");
    }

    private sealed class ThrowingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new IOException("body cut");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
