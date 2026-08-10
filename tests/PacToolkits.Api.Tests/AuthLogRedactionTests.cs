using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PacToolkits.Api.Hosting;

namespace PacToolkits.Api.Tests;

public sealed class AuthLogRedactionTests
{
    [Fact]
    public async Task Token_and_request_logs_omit_api_key_and_jwt()
    {
        var sink = new LogSink();
        await using var factory = new LoggingApiFactory(sink);
        using var client = factory.CreateClient();

        var token = await ApiFactory.FetchAccessTokenAsync(client, TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var ping = await client.GetAsync("/v1/ping", TestContext.Current.CancellationToken);
        ping.EnsureSuccessStatusCode();

        var joined = string.Join('\n', sink.Lines);
        Assert.DoesNotContain(ApiFactory.TestApiKey, joined, StringComparison.Ordinal);
        Assert.DoesNotContain(token, joined, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer ", joined, StringComparison.Ordinal);
    }

    private sealed class LoggingApiFactory(LogSink sink) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("dev");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(ApiFactory.BuildAuthConfig());
            });
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new SinkLoggerProvider(sink));
                logging.SetMinimumLevel(LogLevel.Trace);
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IApiHealth, AlwaysOkApiHealth>();
            });
        }
    }

    private sealed class AlwaysOkApiHealth : IApiHealth
    {
        public Task<ApiHealthSnapshot> CheckAsync(CancellationToken ct = default)
            => Task.FromResult(new ApiHealthSnapshot(Ok: true, Database: "ok", Schema: "ok", SchemaVersion: "1.2.25"));
    }

    private sealed class LogSink
    {
        public ConcurrentBag<string> Lines { get; } = [];
    }

    private sealed class SinkLoggerProvider(LogSink sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName, sink);

        public void Dispose()
        {
        }
    }

    private sealed class SinkLogger(string category, LogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var line = $"{category}|{formatter(state, exception)}";
            if (exception is not null)
            {
                line += "|" + exception;
            }

            sink.Lines.Add(line);
        }
    }
}
