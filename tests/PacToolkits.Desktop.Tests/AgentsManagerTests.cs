using PacToolkits.Agents.Contracts.Abstractions;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Commands;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure.Agents;

namespace PacToolkits.Desktop.Tests;

public sealed class AgentsManagerTests
{
    [Fact]
    public void Registers_by_id()
    {
        var descriptor = new AgentsDescriptor("runtime-a");
        var manager = new AgentsManager([new FakeAgentsRuntime(descriptor)]);

        var runtime = manager.GetRequired("runtime-a");

        Assert.Same(descriptor, runtime.Descriptor);
    }

    [Fact]
    public void GetRequired_by_agents_id()
    {
        var descriptor = AgentsDescriptors.Agents;
        var manager = new AgentsManager([new FakeAgentsRuntime(descriptor)]);

        var runtime = manager.GetRequired(AgentsIds.Agents);

        Assert.Same(descriptor, runtime.Descriptor);
    }

    [Fact]
    public void Rejects_duplicate_id()
    {
        var descriptor = AgentsDescriptors.Agents;

        Assert.Throws<InvalidOperationException>(() =>
            new AgentsManager([
                new FakeAgentsRuntime(descriptor),
                new FakeAgentsRuntime(descriptor),
            ]));
    }

    [Fact]
    public async Task Synchronize_reloads_without_stopping_host()
    {
        var runtime = new FakeAgentsRuntime(AgentsDescriptors.Agents)
        {
            IsHostRunningValue = true,
        };
        var manager = new AgentsManager([runtime]);

        var results = await manager.SyncConfigAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, runtime.ReloadCount);
        Assert.Equal(0, runtime.StopCount);
        Assert.True(results[AgentsIds.Agents].Ok);
        Assert.True(runtime.IsHostRunning);
    }

    [Fact]
    public async Task Stop_all_stops_each_registered_runtime()
    {
        var first = new FakeAgentsRuntime(new AgentsDescriptor("runtime-a"));
        var second = new FakeAgentsRuntime(new AgentsDescriptor("runtime-b"));
        var manager = new AgentsManager([first, second]);

        var results = await manager.StopAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, first.StopCount);
        Assert.Equal(1, second.StopCount);
    }

    private sealed class FakeAgentsRuntime(AgentsDescriptor descriptor) : IAgentsRuntime
    {
        private readonly HashSet<string> _enabledModules = new(StringComparer.Ordinal)
        {
            "module-a",
        };
        private readonly HashSet<string> _runningModules = new(StringComparer.Ordinal);

        public bool IsHostRunningValue { get; set; }
        public int ReloadCount { get; private set; }
        public int StopCount { get; private set; }

        public event Action? StatusChanged
        {
            add { }
            remove { }
        }

        public AgentsDescriptor Descriptor { get; } = descriptor;

        public IReadOnlyList<ModuleDescriptor> Modules => [];

        public string MinDesktop => "1.0.0";

        public string MaxDesktop => "1.0.0";

        public AgentsRunState HostState
            => IsHostRunningValue ? AgentsRunState.Running : AgentsRunState.Stopped;

        public bool IsHostRunning => IsHostRunningValue;

        public DateTimeOffset? HostLastLaunchAt => null;

        public string? HostLastError => null;

        public string HostVersion => "0.0.0";

        public void Dispose()
        {
        }

        public void Reload()
        {
            ReloadCount++;
        }

        public AgentsRunState GetModuleState(string moduleId)
            => _runningModules.Contains(moduleId) ? AgentsRunState.Running : AgentsRunState.Stopped;

        public bool IsModuleEnabled(string moduleId)
            => _enabledModules.Contains(moduleId);

        public DateTimeOffset? GetModuleLastLaunchAt(string moduleId) => null;

        public string? GetModuleLastError(string moduleId) => null;

        public string GetModuleVersion(string moduleId) => "0.0.0";

        public Task<AgentsCommandResult> StartOrRestartAsync(CancellationToken ct = default)
            => Task.FromResult(new AgentsCommandResult(true, "ok"));

        public Task<AgentsCommandResult> StartModuleAsync(string moduleId, CancellationToken ct = default)
        {
            _runningModules.Add(moduleId);
            return Task.FromResult(new AgentsCommandResult(true, "ok"));
        }

        public Task<AgentsCommandResult> StopAsync(CancellationToken ct = default)
        {
            StopCount++;
            IsHostRunningValue = false;
            _runningModules.Clear();
            return Task.FromResult(new AgentsCommandResult(true, "ok"));
        }

        public Task<AgentsCommandResult> StopModuleAsync(string moduleId, CancellationToken ct = default)
        {
            _runningModules.Remove(moduleId);
            return Task.FromResult(new AgentsCommandResult(true, "ok"));
        }
    }
}
