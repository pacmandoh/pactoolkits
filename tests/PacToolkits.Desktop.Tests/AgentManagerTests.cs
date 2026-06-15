using System;
using System.Threading;
using System.Threading.Tasks;
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
    public void GetAgent_by_id()
    {
        var descriptor = AgentDescriptors.InjectorAhk;
        var manager = new AgentManager([new FakeAgentRuntime(descriptor)]);

        var runtime = manager.GetAgent(AgentId.InjectorAhk);

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

    private sealed class FakeAgentRuntime(AgentDescriptor descriptor) : IAgentRuntime
    {
        public event Action? StatusChanged
        {
            add { }
            remove { }
        }

        public AgentDescriptor Descriptor { get; } = descriptor;

        public string ExecutablePath => "agent.exe";

        public ToolRunState State => ToolRunState.Unknown;

        public bool IsRunning => false;

        public DateTimeOffset? LastLaunchAt => null;

        public string? LastError => null;

        public string ToolVersion => "0.0.0";

        public void Dispose()
        {
        }

        public void Reload()
        {
        }

        public Task<ToolCommandResult> StartOrRestartAsync(CancellationToken ct = default)
            => Task.FromResult(new ToolCommandResult(true, "ok"));

        public Task<ToolCommandResult> StopAsync(CancellationToken ct = default)
            => Task.FromResult(new ToolCommandResult(true, "ok"));
    }
}
