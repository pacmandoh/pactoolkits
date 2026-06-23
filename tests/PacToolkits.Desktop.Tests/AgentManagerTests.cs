using PacToolkits.Agent.Contracts.Abstractions;
using PacToolkits.Agent.Contracts.Agents;
using PacToolkits.Agent.Contracts.Commands;
using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class AgentManagerTests
{
    [Fact]
    public void Registers_by_id()
    {
        var descriptor = new AgentDescriptor("agent-a", "Agent A", "test", "agent-a.exe");
        var manager = new AgentManager([new FakeAgentRuntime(descriptor)]);

        var runtime = manager.GetRequired("agent-a");

        Assert.Same(descriptor, runtime.Descriptor);
    }

    [Fact]
    public void GetRequired_by_agent_id()
    {
        var descriptor = AgentDescriptors.InjectorAhk;
        var manager = new AgentManager([new FakeAgentRuntime(descriptor)]);

        var runtime = manager.GetRequired(AgentId.InjectorAhk.Value);

        Assert.Same(descriptor, runtime.Descriptor);
    }

    [Fact]
    public void Rejects_duplicate_id()
    {
        var descriptor = AgentDescriptors.InjectorAhk;

        Assert.Throws<InvalidOperationException>(() =>
            new AgentManager([
                new FakeAgentRuntime(descriptor),
                new FakeAgentRuntime(descriptor),
            ]));
    }

    [Fact]
    public async Task Synchronize_stops_running_agent_after_it_is_disabled()
    {
        var runtime = new FakeAgentRuntime(AgentDescriptors.InjectorAhk)
        {
            IsEnabledValue = false,
            IsRunningValue = true,
        };
        var manager = new AgentManager([runtime]);

        var results = await manager.SyncConfigAsync();

        Assert.Equal(1, runtime.ReloadCount);
        Assert.Equal(1, runtime.StopCount);
        Assert.True(results[AgentIds.InjectorAhk].Ok);
    }

    [Fact]
    public async Task Synchronize_keeps_enabled_running_agent_alive()
    {
        var runtime = new FakeAgentRuntime(AgentDescriptors.InjectorAhk)
        {
            IsEnabledValue = true,
            IsRunningValue = true,
        };
        var manager = new AgentManager([runtime]);

        await manager.SyncConfigAsync();

        Assert.Equal(1, runtime.ReloadCount);
        Assert.Equal(0, runtime.StopCount);
        Assert.True(runtime.IsRunning);
    }

    [Fact]
    public async Task Stop_all_stops_each_registered_agent()
    {
        var first = new FakeAgentRuntime(new AgentDescriptor("agent-a", "A", "test", "a.exe"));
        var second = new FakeAgentRuntime(new AgentDescriptor("agent-b", "B", "test", "b.exe"));
        var manager = new AgentManager([first, second]);

        var results = await manager.StopAllAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(1, first.StopCount);
        Assert.Equal(1, second.StopCount);
    }

    private sealed class FakeAgentRuntime(AgentDescriptor descriptor) : IAgentRuntime
    {
        public bool IsEnabledValue { get; set; } = true;
        public bool IsRunningValue { get; set; }
        public int ReloadCount { get; private set; }
        public int StopCount { get; private set; }

        public event Action? StatusChanged
        {
            add { }
            remove { }
        }

        public AgentDescriptor Descriptor { get; } = descriptor;

        public bool IsEnabled => IsEnabledValue;

        public string MinDbSchema => "1.0.0";

        public string MaxDbSchema => "1.0.0";

        public string ExecutablePath => "agent.exe";

        public ToolRunState State => ToolRunState.Unknown;

        public bool IsRunning => IsRunningValue;

        public DateTimeOffset? LastLaunchAt => null;

        public string? LastError => null;

        public string ToolVersion => "0.0.0";

        public void Dispose()
        {
        }

        public void Reload()
        {
            ReloadCount++;
        }

        public Task<ToolCommandResult> StartOrRestartAsync(CancellationToken ct = default)
            => Task.FromResult(new ToolCommandResult(true, "ok"));

        public Task<ToolCommandResult> StopAsync(CancellationToken ct = default)
        {
            StopCount++;
            IsRunningValue = false;
            return Task.FromResult(new ToolCommandResult(true, "ok"));
        }
    }
}
