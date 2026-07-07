using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Tests;

public sealed class BackgroundTaskRunnerTests
{
    [Fact]
    public async Task RunDetached_logs_and_swallows_unexpected_exception()
    {
        var logger = new CapturingLogger();
        var toast = new NoOpToastService();
        var runner = new BackgroundTaskRunner(logger, toast);

        runner.RunDetached(
            _ => throw new InvalidOperationException("boom"),
            "TestModule",
            "test.background.fail",
            TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal("TestModule", logger.LastModule);
        Assert.Equal("test.background.fail", logger.LastEvent);
        Assert.IsType<InvalidOperationException>(logger.LastException);
    }

    [Fact]
    public async Task RunDetached_swallows_cancellation_without_logging()
    {
        var logger = new CapturingLogger();
        var toast = new NoOpToastService();
        var runner = new BackgroundTaskRunner(logger, toast);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        runner.RunDetached(
            ct => Task.FromCanceled(ct),
            "TestModule",
            "test.background.cancel",
            cts.Token);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Null(logger.LastEvent);
    }

    private sealed class CapturingLogger : IAppLogger
    {
        public string? LastModule { get; private set; }
        public string? LastEvent { get; private set; }
        public Exception? LastException { get; private set; }

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
            LastModule = module;
            LastEvent = eventName;
            LastException = ex;
        }

        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
            => Warn(module, eventName, message, ex, context, traceId);

        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null)
            => Warn(module, eventName, message, ex, context, traceId);

        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class NoOpToastService : IToastService
    {
        public void Info(string title, string message)
        {
        }

        public void Success(string title, string message)
        {
        }

        public void Warn(string title, string message)
        {
        }

        public void Error(string title, string message)
        {
        }
    }

}
