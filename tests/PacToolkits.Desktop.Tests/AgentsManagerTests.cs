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
        public bool IsInjectorEnabledValue { get; set; } = true;
        public bool IsHostRunningValue { get; set; }
        public bool IsInjectorRunningValue { get; set; }
        public int ReloadCount { get; private set; }
        public int StopCount { get; private set; }

        public event Action? StatusChanged
        {
            add { }
            remove { }
        }

        public AgentsDescriptor Descriptor { get; } = descriptor;

        public bool IsInjectorEnabled => IsInjectorEnabledValue;

        public string MinDbSchema => "1.0.0";

        public string MaxDbSchema => "1.0.0";

        public AgentsRunState HostState
            => IsHostRunningValue ? AgentsRunState.Running : AgentsRunState.Stopped;

        public AgentsRunState InjectorState
            => IsInjectorRunningValue ? AgentsRunState.Running : AgentsRunState.Stopped;

        public bool IsHostRunning => IsHostRunningValue;

        public bool IsInjectorRunning => IsInjectorRunningValue;

        public DateTimeOffset? HostLastLaunchAt => null;

        public DateTimeOffset? InjectorLastLaunchAt => null;

        public string? HostLastError => null;

        public string? InjectorLastError => null;

        public string HostVersion => "0.0.0";

        public string InjectorVersion => "0.0.0";

        public void Dispose()
        {
        }

        public void Reload()
        {
            ReloadCount++;
        }

        public Task<AgentsCommandResult> StartOrRestartAsync(CancellationToken ct = default)
            => Task.FromResult(new AgentsCommandResult(true, "ok"));

        public Task<AgentsCommandResult> StartInjectorAsync(CancellationToken ct = default)
        {
            IsInjectorRunningValue = true;
            return Task.FromResult(new AgentsCommandResult(true, "ok"));
        }

        public Task<AgentsCommandResult> StopAsync(CancellationToken ct = default)
        {
            StopCount++;
            IsHostRunningValue = false;
            IsInjectorRunningValue = false;
            return Task.FromResult(new AgentsCommandResult(true, "ok"));
        }

        public Task<AgentsCommandResult> StopInjectorAsync(CancellationToken ct = default)
        {
            IsInjectorRunningValue = false;
            return Task.FromResult(new AgentsCommandResult(true, "ok"));
        }
    }
}
