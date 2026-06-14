using System.Diagnostics;
using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Agents;
using PacToolkits.Agent.Contracts.Events;
using PacToolkits.Agent.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Application;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Tests;

public sealed class AhkInjectorAgentRuntimeTests
{
    [Fact]
    public async Task Start_when_disabled()
    {
        var config = new FakeAppConfigStore
        {
            Root = new AppConfigRoot
            {
                Agents =
                {
                    [AgentIds.InjectorAhk] = new AgentInstanceConfig
                    {
                        Enabled = false,
                        ExecutablePath = @"C:\Apps\Agents\injector\pactoolkits-injector.exe",
                    },
                },
            },
        };

        using var runtime = new AhkInjectorAgentRuntime(
            config,
            new FakeReleaseVersionService(),
            new NullAppLogger(),
            new NullAgentEventSink());

        var result = await runtime.StartOrRestartAsync();

        Assert.False(result.Ok);
        Assert.Contains("禁用", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchesExe_wrong_path()
    {
        using var process = Process.GetCurrentProcess();
        var currentExe = process.MainModule?.FileName;
        Assert.False(string.IsNullOrWhiteSpace(currentExe));

        Assert.True(AhkInjectorAgentRuntime.MatchesExe(
            process,
            currentExe,
            permissiveOnAccessDenied: false));
        Assert.False(AhkInjectorAgentRuntime.MatchesExe(
            process,
            @"C:\Other\agent.exe",
            permissiveOnAccessDenied: false));
    }

    private sealed class FakeAppConfigStore : IAppConfigStore
    {
        public AppConfigRoot Root { get; set; } = new();

        public string ConfigPath { get; } = "/tmp/pactoolkits-test.config.json";

        public AppConfigRoot Load() => Root;

        public void Save(AppConfigRoot config) => Root = config;

        public Task SaveAsync(AppConfigRoot config, CancellationToken ct = default)
        {
            Root = config;
            return Task.CompletedTask;
        }

        public void Update(Action<AppConfigRoot> mutator)
        {
            mutator(Root);
        }

        public Task UpdateAsync(Action<AppConfigRoot> mutator, CancellationToken ct = default)
        {
            mutator(Root);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeReleaseVersionService : IReleaseVersionService
    {
        public ReleaseVersionInfo Current { get; } = new(
            ProductVersion: "0.17.1",
            DesktopVersion: "0.16.1",
            AgentInjectorAhkVersion: "0.6.1",
            DatabasePostgresVersion: "1.2.22",
            BuildChannel: "stable",
            BuildDate: "2026-06-13",
            DesktopMinDbSchema: "1.2.22",
            AgentInjectorAhkMinDbSchema: "1.2.22");
    }

    private sealed class NullAppLogger : IAppLogger
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

    private sealed class NullAgentEventSink : IAgentEventSink
    {
        public void Publish(AgentExecutionEvent executionEvent)
        {
        }
    }
}
