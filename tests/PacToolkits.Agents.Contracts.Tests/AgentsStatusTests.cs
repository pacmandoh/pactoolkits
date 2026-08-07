using PacToolkits.Agents.Contracts.Agents;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class AgentsStatusTests
{
    [Fact]
    public void Write_and_TryRead_round_trip()
    {
        var dir = CreateTempDir();
        try
        {
            var written = AgentsStatus.Create(
                hostPid: 42,
                modules:
                [
                    new AgentsStatusModule
                    {
                        Id = "Injector",
                        Pid = 100,
                        Ready = true,
                        State = AgentsRunState.Running,
                        Version = "1.2.3",
                        DisplayName = "注入器",
                        IconActive = "on.png",
                        Order = 1,
                    },
                    new AgentsStatusModule
                    {
                        Id = "Scanner",
                        Pid = null,
                        Ready = false,
                        State = AgentsRunState.Failed,
                        LastError = "no entry",
                    },
                ]);
            written.HostState = AgentsRunState.Running;

            AgentsStatus.Write(dir, written);

            var read = AgentsStatus.TryRead(dir, maxAge: TimeSpan.FromMinutes(1));
            Assert.NotNull(read);
            Assert.Equal(AgentsStatus.CurrentSchema, read.SchemaVersion);
            Assert.Equal(42, read.HostPid);
            Assert.Equal(AgentsRunState.Running, read.HostState);
            Assert.Equal(2, read.Modules.Count);
            var injector = read.FindModule("Injector");
            Assert.NotNull(injector);
            Assert.Equal(100, injector.Pid);
            Assert.True(injector.Ready);
            Assert.True(injector.ProcessAlive);
            Assert.Equal(AgentsRunState.Running, injector.State);
            Assert.Equal("1.2.3", injector.Version);
            Assert.Equal("注入器", injector.DisplayName);
            var scanner = read.FindModule("Scanner");
            Assert.NotNull(scanner);
            Assert.False(scanner.ProcessAlive);
            Assert.Equal(AgentsRunState.Failed, scanner.State);
            Assert.Equal("no entry", scanner.LastError);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void TryRead_returns_null_when_stale()
    {
        var dir = CreateTempDir();
        try
        {
            var status = AgentsStatus.Create(1, []);
            status.Ts = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10);
            AgentsStatus.Write(dir, status);

            Assert.Null(AgentsStatus.TryRead(dir, maxAge: TimeSpan.FromSeconds(2)));
            Assert.NotNull(AgentsStatus.TryRead(dir, maxAge: TimeSpan.FromMinutes(1)));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void TryDelete_removes_status_file()
    {
        var dir = CreateTempDir();
        try
        {
            AgentsStatus.Write(dir, AgentsStatus.Create(1, []));
            Assert.True(File.Exists(AgentsPaths.HostStatusPath(dir)));
            AgentsStatus.TryDelete(dir);
            Assert.False(File.Exists(AgentsPaths.HostStatusPath(dir)));
            Assert.Null(AgentsStatus.TryRead(dir));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pac-agents-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // ignore
        }
    }
}
