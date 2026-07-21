using System.Diagnostics;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

namespace PacToolkits.Desktop.Tests;

public sealed class AgentsRuntimeTests
{
    [Fact]
    public void Declares_independent_database_compatibility_range()
    {
        using var runtime = new AgentsRuntime(
            new FakeAppConfigStore(),
            new FakeReleaseVersionService(),
            new FakeDbSchemaVersionService(),
            new DbMigrationPolicyService(new FakeDbEnvSettingsService()),
            new NullAppLogger());

        Assert.True(runtime.IsInjectorEnabled);
        Assert.Equal("1.2.22", runtime.MinDbSchema);
        Assert.Equal("1.2.22", runtime.MaxDbSchema);
    }

    [Fact]
    public async Task Start_when_database_schema_is_below_minimum()
    {
        var config = new FakeAppConfigStore();
        using var runtime = new AgentsRuntime(
            config,
            new FakeReleaseVersionService(),
            new FakeDbSchemaVersionService("1.2.20"),
            new DbMigrationPolicyService(new FakeDbEnvSettingsService()),
            new NullAppLogger());

        var result = await runtime.StartOrRestartAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains("低于最低支持版本", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_when_database_schema_is_above_agent_maximum()
    {
        var config = new FakeAppConfigStore();
        using var runtime = new AgentsRuntime(
            config,
            new FakeReleaseVersionService(),
            new FakeDbSchemaVersionService("1.2.23"),
            new DbMigrationPolicyService(new FakeDbEnvSettingsService()),
            new NullAppLogger());

        var result = await runtime.StartOrRestartAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains("数据库版本高于当前程序支持范围", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchesExe_wrong_path()
    {
        using var process = Process.GetCurrentProcess();
        var currentExe = process.MainModule?.FileName;
        Assert.False(string.IsNullOrWhiteSpace(currentExe));

        Assert.True(AgentsRuntime.MatchesExe(
            process,
            currentExe,
            permissiveOnAccessDenied: false));
        Assert.False(AgentsRuntime.MatchesExe(
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
            AgentsVersion: "0.6.1",
            DbSchemaVersion: "1.2.22",
            BuildChannel: "stable",
            BuildDate: "2026-06-13",
            DesktopMinDbSchema: "1.2.22",
            DesktopMaxDbSchema: "1.2.22",
            AgentsMinDbSchema: "1.2.22",
            AgentsMaxDbSchema: "1.2.22",
            DbMigrationPolicy: DbMigrationPolicies.StableOnly);
    }

    private sealed class FakeDbSchemaVersionService(string version = "1.2.22") : IDbSchemaVersionService
    {
        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(CancellationToken ct)
            => Task.FromResult(new DbSchemaVersionRead(true, version, null));

        public Task<DbSchemaVersionRead> TryReadSchemaVersionAsync(PgOptions options, CancellationToken ct)
            => Task.FromResult(new DbSchemaVersionRead(true, version, null));
    }

    private sealed class FakeDbEnvSettingsService : IDbEnvSettingsService
    {
        public Task<DbEnvSettings> TryReadAsync(CancellationToken ct)
            => Task.FromResult(DbEnvSettings.ProductionDefaults);

        public Task<DbEnvSettings> TryReadAsync(PgOptions options, CancellationToken ct)
            => Task.FromResult(DbEnvSettings.ProductionDefaults);
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
}
