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
    public void ContentEquals_ignores_timestamp_and_scope_list_instance()
    {
        var left = AgentsStatus.Create(
            7,
            [
                new AgentsStatusModule
                {
                    Id = "Injector",
                    Pid = 11,
                    Ready = true,
                    State = AgentsRunState.Running,
                    RequiredApiScopes = ["read", "write"],
                },
            ]);
        left.Ts = DateTimeOffset.UtcNow.AddSeconds(-1);

        var right = AgentsStatus.Create(
            7,
            [
                new AgentsStatusModule
                {
                    Id = "Injector",
                    Pid = 11,
                    Ready = true,
                    State = AgentsRunState.Running,
                    RequiredApiScopes = new List<string> { "read", "write" },
                },
            ]);

        Assert.True(AgentsStatus.ContentEquals(left, right));
    }

    [Fact]
    public void ContentEquals_detects_module_state_change()
    {
        var left = AgentsStatus.Create(
            1,
            [new AgentsStatusModule { Id = "Injector", State = AgentsRunState.Running, Pid = 1 }]);
        var right = AgentsStatus.Create(
            1,
            [new AgentsStatusModule { Id = "Injector", State = AgentsRunState.Stopped, Pid = null }]);

        Assert.False(AgentsStatus.ContentEquals(left, right));
    }

    [Fact]
    public void Clone_copies_mutable_collections()
    {
        var source = AgentsStatus.Create(
            7,
            [new AgentsStatusModule { Id = "Injector", RequiredApiScopes = ["read"] }]);

        var clone = source.Clone();

        Assert.NotSame(source, clone);
        Assert.NotSame(source.Modules, clone.Modules);
        Assert.NotSame(source.Modules[0], clone.Modules[0]);
        Assert.NotSame(source.Modules[0].RequiredApiScopes, clone.Modules[0].RequiredApiScopes);
        Assert.True(AgentsStatus.ContentEquals(source, clone));
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
