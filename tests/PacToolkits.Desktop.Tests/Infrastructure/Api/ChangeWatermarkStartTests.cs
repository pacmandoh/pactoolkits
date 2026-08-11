using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Infrastructure.Database;

namespace PacToolkits.Desktop.Tests;

public sealed class ChangeWatermarkStartTests
{
    [Fact]
    public async Task Concurrent_Start_starts_each_loop_once()
    {
        using var svc = new ChangeWatermarkService(
            new EmptyWatermarkRepo(),
            new StubDbConfig(),
            new NullLogger(),
            TimeProvider.System);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(svc.Start)));
        await Task.Delay(80, TestContext.Current.CancellationToken);

        Assert.Equal(1, svc.TestDispatchLoopStarts);
        Assert.Equal(1, svc.TestPollLoopStarts);
        Assert.Equal(1, svc.TestListenLoopStarts);
    }

    private sealed class EmptyWatermarkRepo : IChangeWatermarkRepo
    {
        public Task<IReadOnlyList<ChangeWatermarkItem>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChangeWatermarkItem>>([]);
    }

    private sealed class StubDbConfig : IDbConfigService
    {
        public PgOptions Current { get; } = new()
        {
            Host = "127.0.0.1",
            Port = 1,
            ConnectTimeoutSeconds = 1,
        };

        public string ConfigPath => "/tmp/pg-options-test.json";

        public event EventHandler? Applied
        {
            add { }
            remove { }
        }

        public Task<bool> TestConnectionAsync(PgOptions opt, CancellationToken ct)
            => Task.FromResult(false);

        public Task ApplyAsync(PgOptions opt, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/change-watermark-start-test.log";
        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
