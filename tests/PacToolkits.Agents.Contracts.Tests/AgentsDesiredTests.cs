using PacToolkits.Agents.Contracts.Agents;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class AgentsDesiredTests
{
    [Fact]
    public void Write_and_TryReadIds_round_trip()
    {
        var dir = CreateTempDir();
        try
        {
            AgentsDesired.Write(dir, ["Injector", "Scanner", "Injector"]);
            var ids = AgentsDesired.TryReadIds(dir);
            Assert.NotNull(ids);
            Assert.Equal(2, ids.Count);
            Assert.Contains("Injector", ids);
            Assert.Contains("Scanner", ids);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void TryDelete_removes_desired()
    {
        var dir = CreateTempDir();
        try
        {
            AgentsDesired.Write(dir, ["A"]);
            Assert.True(File.Exists(AgentsPaths.HostDesiredPath(dir)));
            AgentsDesired.TryDelete(dir);
            Assert.Null(AgentsDesired.TryReadIds(dir));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pac-agents-desired-" + Guid.NewGuid().ToString("N"));
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
