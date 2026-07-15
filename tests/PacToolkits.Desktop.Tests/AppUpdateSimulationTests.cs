using PacToolkits.Application.Abstractions;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Integration.Update;
using PacToolkits.Desktop.Avalonia.Services.Presentation;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Tests;

public sealed class AppUpdateSimulationTests
{
    [Fact]
    public async Task CheckAsync_PublishesAvailableState()
    {
        using var service = new AppUpdateService(
            new UpdateSettings(),
            new NullLogger(),
            "1.0.3-beta.1");
        var changed = 0;
        service.Changed += () => changed++;

        var result = await service.CheckAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.True(result.HasUpdate);
        Assert.True(result.HasProductUpdate);
        Assert.Equal("1.0.3-beta.1", result.LatestVersion);
        Assert.Equal("ui-simulation", result.Source);
        Assert.True(service.HasUpdateAvailable);
        Assert.Equal("1.0.3-beta.1", service.LatestVersion);
        Assert.True(changed >= 2);
    }

    [Fact]
    public async Task ApplyAsync_BlocksRealUpdateWork()
    {
        using var service = new AppUpdateService(
            new UpdateSettings(),
            new NullLogger(),
            "1.0.3-beta.1");
        var progress = new CaptureProgress();

        var result = await service.ApplyAsync(progress, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.False(result.RequiresRestart);
        Assert.Equal("1.0.3-beta.1", result.TargetVersion);
        Assert.Contains("模拟模式", result.Message, StringComparison.Ordinal);
        Assert.Equal([0, 20, 40, 60, 80, 100], progress.Values);
        Assert.Equal(
            ["更新 · 0%", "更新 · 20%", "更新 · 40%", "更新 · 60%", "更新 · 80%", "更新 · 100%"],
            progress.Values.Select(value => MainWindowViewModel.GetUpdateText(true, value)));
        Assert.Equal("更新", MainWindowViewModel.GetUpdateText(false, 100));
    }

    [Fact]
    public async Task ApplyFlow_ExposesSimulatedProgressForTitleBar()
    {
        using var updates = new AppUpdateService(
            new UpdateSettings(),
            new NullLogger(),
            "1.0.3-beta.1");
        var flow = new UpdateFlowService(
            updates,
            new NoOpToast(),
            static (_, _) => Task.FromResult(false),
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

    private sealed class CaptureProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value) => Values.Add(value);
    }

    private sealed class UpdateSettings : IUpdateSettingsService
    {
        public UpdateOptions Current { get; } = new();

        public event Action? Changed;

        public Task SaveAsync(UpdateOptions options, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveIgnoredVersionAsync(string version, CancellationToken ct = default)
            => Task.CompletedTask;

        public void Reload() => Changed?.Invoke();
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
