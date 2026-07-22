using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Integration.Update;
using PacToolkits.Desktop.Avalonia.Services.Presentation;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Tests;

public sealed class UpdateFlowServiceTests
{
    [Fact]
    public async Task ApplyUpdateFlowAsync_ExposesDownloadProgressForTitleBar()
    {
        var updates = new ProgressUpdateService();
        var flow = new UpdateFlowService(
            updates,
            new NoOpToast(),
            new NullLogger());
        var states = new List<(bool Applying, int Progress, string Text)>();
        flow.StateChanged += () => states.Add((
            flow.IsApplying,
            flow.ApplyProgress,
            MainWindowViewModel.GetUpdateText(flow.IsApplying, flow.ApplyProgress)));

        await flow.ApplyUpdateFlowAsync();

        Assert.Contains(states, state => state is { Applying: true, Progress: 20, Text: "更新 · 20%" });
        Assert.Contains(states, state => state is { Applying: true, Progress: 100, Text: "更新 · 100%" });
        Assert.Equal((false, 0, "更新"), states[^1]);
    }

    private sealed class ProgressUpdateService : IAppUpdateService
    {
        public string CurrentVersion => "1.0.2";
        public string LatestVersion => "1.0.3";
        public bool HasUpdateAvailable => true;
        public bool? UpdateAvailability => true;
        public bool IsChecking => false;
        public DateTimeOffset? LastCheckedAt => DateTimeOffset.Now;
        public string LastMessage => string.Empty;
        public UpdateTargetState? Target => null;

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public Task<AppUpdateCheckResult> CheckAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppUpdateApplyResult> ApplyAsync(
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            progress?.Report(20);
            progress?.Report(100);
            return Task.FromResult(new AppUpdateApplyResult(true, "更新包已准备完成"));
        }

        public Task<bool> RestartToApplyAsync(CancellationToken ct = default)
            => Task.FromResult(true);

        public Task AlignChannelAsync(
            Func<string, string, CancellationToken, Task<bool>>? confirmMismatchAsync = null,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpToast : IToastService
    {
        public void Success(string title, string message) { }
        public void Error(string title, string message) { }
        public void Warn(string title, string message) { }
        public void Info(string title, string message) { }
    }

    private sealed class NullLogger : IAppLogger
    {
        public string LogDirectory => "/tmp";
        public string CurrentLogPath => "/tmp/pactoolkits-test.log";

        public void Debug(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Info(string module, string eventName, string message, object? context = null, string? traceId = null) { }
        public void Warn(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Error(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }
        public void Fatal(string module, string eventName, string message, Exception? ex = null, object? context = null, string? traceId = null) { }

        public Task<string> ExportRecentAsync(TimeSpan window, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
    }
}
