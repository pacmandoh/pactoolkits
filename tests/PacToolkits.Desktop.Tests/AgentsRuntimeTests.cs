using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

namespace PacToolkits.Desktop.Tests;

public sealed class AgentsRuntimeTests
{
    private const string TestModuleId = "ModuleA";

    [Fact]
    public void Declares_agents_desktop_compatibility_range()
    {
        using var runtime = new AgentsRuntime(
            new FakeAppConfigStore(),
            new FakeModuleSettingsStore(),
            new FakeReleaseVersionService(),
            new NullAppLogger());

        Assert.True(runtime.IsModuleEnabled(TestModuleId));
        Assert.Equal("0.16.1", runtime.MinDesktop);
        Assert.Equal("0.16.1", runtime.MaxDesktop);
    }

    [Fact]
    public async Task Start_when_desktop_outside_agents_range()
    {
        var config = new FakeAppConfigStore();
        using var runtime = new AgentsRuntime(
            config,
            new FakeModuleSettingsStore(),
            new FakeReleaseVersionService(
                desktopVersion: "0.15.0",
                agentsMinDesktop: "0.16.0",
                agentsMaxDesktop: "0.17.0"),
            new NullAppLogger());

        var result = await runtime.StartOrRestartAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains("支持下限", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("数据库版本", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_rejects_prerelease_below_stable_min_desktop()
    {
        using var runtime = new AgentsRuntime(
            new FakeAppConfigStore(),
            new FakeModuleSettingsStore(),
            new FakeReleaseVersionService(
                desktopVersion: "1.0.2-beta.8",
                agentsMinDesktop: "1.0.2",
                agentsMaxDesktop: "1.0.5"),
            new NullAppLogger());

        var result = await runtime.StartOrRestartAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains("支持下限", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_allows_ordered_beta_inside_desktop_range()
    {
        using var runtime = new AgentsRuntime(
            new FakeAppConfigStore(),
            new FakeModuleSettingsStore(),
            new FakeReleaseVersionService(
                desktopVersion: "1.0.3-beta.8",
                agentsMinDesktop: "1.0.2",
                agentsMaxDesktop: "1.0.5"),
            new NullAppLogger());

        var result = await runtime.StartOrRestartAsync(TestContext.Current.CancellationToken);

        // 通过配套门禁；后续平台/路径/OS 门禁可另报
        Assert.DoesNotContain("支持下限", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("支持上限", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("无法校验 Agents 与 Desktop", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_does_not_gate_on_database_schema()
    {
        var config = new FakeAppConfigStore();
        using var runtime = new AgentsRuntime(
            config,
            new FakeModuleSettingsStore(),
            // unknown desktop bounds：跳过配套门禁，后续平台/路径门禁可另报
            new FakeReleaseVersionService(
                desktopVersion: "unknown",
                agentsMinDesktop: "unknown",
                agentsMaxDesktop: "unknown"),
            new NullAppLogger());

        var result = await runtime.StartOrRestartAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("数据库版本", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("低于最低支持版本", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed to connect", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_rejects_incomplete_agents_desktop_bounds()
    {
        using var runtime = new AgentsRuntime(
            new FakeAppConfigStore(),
            new FakeModuleSettingsStore(),
            new FakeReleaseVersionService(
                desktopVersion: "1.0.2",
                agentsMinDesktop: "1.0.2",
                agentsMaxDesktop: string.Empty),
            new NullAppLogger());

        var result = await runtime.StartOrRestartAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.Contains("不完整", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Min_max_desktop_expose_raw_agents_bounds_without_desktop_fallback()
    {
        using var runtime = new AgentsRuntime(
            new FakeAppConfigStore(),
            new FakeModuleSettingsStore(),
            new FakeReleaseVersionService(
                desktopVersion: "1.0.2",
                agentsMinDesktop: string.Empty,
                agentsMaxDesktop: "unknown"),
            new NullAppLogger());

        Assert.Equal(string.Empty, runtime.MinDesktop);
        Assert.Equal("unknown", runtime.MaxDesktop);
    }

    [Fact]
    public void Binary_change_requires_two_stable_active_observations()
    {
        var change = new AgentsBinaryChange();
        var original = new AgentsBinaryStamp(100, 10);
        var updated = new AgentsBinaryStamp(120, 20);

        Assert.False(change.Observe(original, active: false, out _));
        Assert.False(change.Observe(updated, active: true, out _));
        Assert.True(change.Observe(updated, active: true, out var detected));
        Assert.Equal(updated, detected);
        Assert.True(change.Accept(detected));
        Assert.False(change.Observe(updated, active: true, out _));
    }

    [Fact]
    public void Inactive_binary_change_advances_baseline_without_reload()
    {
        var change = new AgentsBinaryChange();
        var original = new AgentsBinaryStamp(100, 10);
        var updated = new AgentsBinaryStamp(120, 20);

        Assert.False(change.Observe(original, active: false, out _));
        Assert.False(change.Observe(updated, active: false, out _));
        Assert.False(change.Observe(updated, active: true, out _));
    }

    [Fact]
    public void Missing_binary_does_not_replace_accepted_baseline()
    {
        var change = new AgentsBinaryChange();
        var original = new AgentsBinaryStamp(100, 10);
        var updated = new AgentsBinaryStamp(120, 20);

        Assert.False(change.Observe(original, active: false, out _));
        Assert.False(change.Observe(null, active: true, out _));
        Assert.False(change.Observe(updated, active: true, out _));
        Assert.True(change.Observe(updated, active: true, out _));
    }

    [Theory]
    [InlineData(AgentsRunState.Starting, true)]
    [InlineData(AgentsRunState.Running, true)]
    [InlineData(AgentsRunState.Stopped, false)]
    [InlineData(AgentsRunState.Failed, false)]
    [InlineData(AgentsRunState.Unknown, false)]
    public void Binary_reload_activity_matches_runtime_lifecycle(AgentsRunState state, bool expected)
    {
        Assert.Equal(expected, state.IsActive());
    }

    [Fact]
    public void Module_orphans_clear_empty_catalog_is_ok()
    {
        Assert.True(AgentsModuleOrphans.Clear(new AgentsOptions(), [], new NullAppLogger()));
    }

    [Fact]
    public void Desktop_compat_rejects_below_min()
    {
        var result = AgentsDesktopCompat.Validate("1.0.0", "1.0.2", "1.0.5");
        Assert.False(result.Ok);
        Assert.Contains("支持下限", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_compat_allows_ordered_prerelease()
    {
        var result = AgentsDesktopCompat.Validate("1.0.3-beta.8", "1.0.2", "1.0.5");
        Assert.True(result.Ok, result.Message);
    }

    [Fact]
    public void Module_discovery_empty_dir_keeps_empty_catalog()
    {
        Assert.Empty(AgentsStatusCatalog.ToDescriptors(AgentsStatus.Create(1, []), agentsDir: "/x"));
    }

    [Theory]
    [InlineData(true, false, false, AgentsRunState.Running)]
    [InlineData(false, true, false, AgentsRunState.Starting)]
    [InlineData(false, false, true, AgentsRunState.Failed)]
    [InlineData(false, false, false, AgentsRunState.Stopped)]
    public void RunObserve_host(
        bool processAlive,
        bool launching,
        bool stickyFailed,
        AgentsRunState expected)
    {
        Assert.Equal(expected, AgentsObserve.Host(processAlive, launching, stickyFailed));
    }

    [Fact]
    public void RunObserve_module_running_clears_errors()
    {
        var resultState = AgentsObserve.Module(
            desired: true,
            processAlive: true,
            ready: true,
            startFailed: false,
            lastError: null);

        Assert.Equal(AgentsRunState.Running, resultState);
    }

    [Fact]
    public void RunObserve_module_stale_ready_is_failed_or_starting()
    {
        var state = AgentsObserve.Module(
            desired: true,
            processAlive: false,
            ready: true,
            startFailed: false,
            lastError: null);

        Assert.Equal(AgentsRunState.Starting, state);
    }

    [Fact]
    public void RunObserve_module_user_drop_desired_is_stopped()
    {
        var state = AgentsObserve.Module(
            desired: false,
            processAlive: true,
            ready: true,
            startFailed: true,
            lastError: "x");

        Assert.Equal(AgentsRunState.Stopped, state);
    }

    [Fact]
    public void OptionsModel_normalize_trims_and_clones_modules()
    {
        var src = new AgentsOptions
        {
            ExecutablePath = " /agents/host ",
            ProcessName = " PacTools.Agents ",
            Modules = new Dictionary<string, ModuleOptions>(StringComparer.Ordinal)
            {
                ["Injector"] = new ModuleOptions { Enabled = true },
            },
        };

        var normalized = AgentsOptionsModel.Normalize(src);
        Assert.Equal("/agents/host", normalized.ExecutablePath);
        Assert.Equal("PacTools.Agents", normalized.ProcessName);
        Assert.True(normalized.Modules["Injector"].Enabled);

        src.Modules["Injector"].Enabled = false;
        Assert.True(normalized.Modules["Injector"].Enabled);

        Assert.True(AgentsOptionsModel.Same(normalized, AgentsOptionsModel.Clone(new AgentsOptions
        {
            ExecutablePath = " /agents/host ",
            ProcessName = " PacTools.Agents ",
            Modules = new Dictionary<string, ModuleOptions>(StringComparer.Ordinal)
            {
                ["Injector"] = new ModuleOptions { Enabled = true },
            },
        })));
    }

    private sealed class FakeAppConfigStore : IAppConfigStore
    {
        public AppConfigRoot Root { get; set; } = CreateRoot();

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

        private static AppConfigRoot CreateRoot()
        {
            var root = new AppConfigRoot();
            root.Agents.Modules[TestModuleId] =
                new PacToolkits.Agents.Contracts.Models.ModuleOptions { Enabled = true };
            return root;
        }
    }

    private sealed class FakeReleaseVersionService : IReleaseVersionService
    {
        public FakeReleaseVersionService(
            string desktopVersion = "0.16.1",
            string agentsMinDesktop = "0.16.1",
            string agentsMaxDesktop = "0.16.1")
        {
            Current = new(
                ProductVersion: "0.17.1",
                DesktopVersion: desktopVersion,
                AgentsVersion: "0.6.1",
                DbSchemaVersion: "1.2.22",
                BuildChannel: "stable",
                BuildDate: "2026-06-13",
                DesktopMinDbSchema: "1.2.22",
                DesktopMaxDbSchema: "1.2.22",
                AgentsMinDesktop: agentsMinDesktop,
                AgentsMaxDesktop: agentsMaxDesktop);
        }

        public ReleaseVersionInfo Current { get; }
    }

    private sealed class FakeModuleSettingsStore : IModuleSettingsStore
    {
        public void EnsureUserSettings(string moduleId, string agentsDir)
        {
        }

        public string LoadSettingsJson(string moduleId) => "{}";

        public Task SaveSettingsJsonAsync(string moduleId, string json, CancellationToken ct = default)
            => Task.CompletedTask;

        public string? TryLoadSchemaJson(string moduleId, string agentsDir) => null;
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
