using PacToolkits.Agents.Contracts.Agents;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class AgentsObserveTests
{
    [Theory]
    [InlineData(true, false, false, AgentsRunState.Running)]
    [InlineData(false, true, false, AgentsRunState.Starting)]
    [InlineData(false, false, true, AgentsRunState.Failed)]
    [InlineData(false, false, false, AgentsRunState.Stopped)]
    public void Host_facts(
        bool processAlive,
        bool launching,
        bool stickyFailed,
        AgentsRunState expected)
        => Assert.Equal(expected, AgentsObserve.Host(processAlive, launching, stickyFailed));

    [Fact]
    public void Module_running()
        => Assert.Equal(
            AgentsRunState.Running,
            AgentsObserve.Module(desired: true, processAlive: true, ready: true, startFailed: false, lastError: null));

    [Fact]
    public void Module_desired_starting()
        => Assert.Equal(
            AgentsRunState.Starting,
            AgentsObserve.Module(desired: true, processAlive: true, ready: false, startFailed: false, lastError: null));

    [Fact]
    public void Module_start_failed()
        => Assert.Equal(
            AgentsRunState.Failed,
            AgentsObserve.Module(
                desired: true,
                processAlive: false,
                ready: false,
                startFailed: true,
                lastError: "boom"));

    [Fact]
    public void Module_not_desired_is_stopped_even_with_error()
        => Assert.Equal(
            AgentsRunState.Stopped,
            AgentsObserve.Module(
                desired: false,
                processAlive: true,
                ready: true,
                startFailed: true,
                lastError: "x"));
}
