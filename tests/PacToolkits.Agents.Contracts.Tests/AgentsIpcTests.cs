using PacToolkits.Agents.Contracts.Agents;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class AgentsIpcTests
{
    [Fact]
    public void PipeName_is_stable_for_same_dir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pac-agents-ipc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var a = AgentsIpc.PipeName(dir);
            var b = AgentsIpc.PipeName(dir);
            Assert.Equal(a, b);
            Assert.StartsWith(AgentsIpc.PipeNamePrefix, a, StringComparison.Ordinal);
            Assert.NotEqual(a, AgentsIpc.PipeName(Path.Combine(dir, "other")));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void Desired_round_trips_json()
    {
        var message = AgentsIpc.Desired(["Injector", "Scanner"], id: "abc");
        var line = AgentsIpc.Serialize(message);
        var read = AgentsIpc.TryDeserialize(line);
        Assert.NotNull(read);
        Assert.Equal(AgentsIpcOps.Desired, read.Op);
        Assert.Equal("abc", read.Id);
        Assert.Equal(2, read.Modules!.Count);
        Assert.Contains("Injector", read.Modules);
    }

    [Fact]
    public void ModuleFailedEvent_embeds_module_id()
    {
        var status = AgentsStatus.Create(
            1,
            [new AgentsStatusModule { Id = "M", State = AgentsRunState.Failed, LastError = "boom" }]);
        var line = AgentsIpc.Serialize(AgentsIpc.ModuleFailedEvent("M", "boom", status));
        Assert.Contains("\"ev\":\"moduleFailed\"", line, StringComparison.OrdinalIgnoreCase);
        var read = AgentsIpc.TryDeserialize(line);
        Assert.NotNull(read);
        Assert.Equal(AgentsIpcEvs.ModuleFailed, read.Ev);
        Assert.Equal("M", read.ModuleId);
        Assert.Equal("boom", read.Message);
    }

    [Fact]
    public void StatusEvent_embeds_status_with_enum_strings()
    {
        var status = AgentsStatus.Create(
            9,
            [
                new AgentsStatusModule
                {
                    Id = "M",
                    Pid = 1,
                    Ready = true,
                    State = AgentsRunState.Running,
                    LastError = null,
                    Version = "1.0.0",
                    DisplayName = "模块",
                },
            ]);
        status.HostState = AgentsRunState.Running;
        var line = AgentsIpc.Serialize(AgentsIpc.StatusEvent(status, id: "s1"));
        Assert.Contains("\"state\":\"running\"", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"hostState\":\"running\"", line, StringComparison.OrdinalIgnoreCase);

        var read = AgentsIpc.TryDeserialize(line);
        Assert.NotNull(read);
        Assert.Equal(AgentsIpcEvs.Status, read.Ev);
        Assert.Equal(9, read.Status!.HostPid);
        Assert.Equal(AgentsRunState.Running, read.Status.HostState);
        var m = read.Status.FindModule("M");
        Assert.NotNull(m);
        Assert.True(m.Ready);
        Assert.Equal(AgentsRunState.Running, m.State);
        Assert.Equal("1.0.0", m.Version);
    }

    [Fact]
    public async Task ReadAsync_keeps_second_frame_in_buffer()
    {
        var first = AgentsIpc.Ping("a");
        var second = AgentsIpc.Pong("a");
        var payload = AgentsIpc.Serialize(first) + "\n" + AgentsIpc.Serialize(second) + "\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload));
        var buffer = new AgentsIpcReadBuffer();
        var readFirst = await AgentsIpcStream.ReadAsync(stream, buffer, TestContext.Current.CancellationToken);
        var readSecond = await AgentsIpcStream.ReadAsync(stream, buffer, TestContext.Current.CancellationToken);
        Assert.NotNull(readFirst);
        Assert.NotNull(readSecond);
        Assert.Equal(AgentsIpcOps.Ping, readFirst.Op);
        Assert.Equal(AgentsIpcEvs.Pong, readSecond.Ev);
    }
}
