using PacToolkits.Agents.Contracts.Agents;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class AgentsLogPathsTests
{
    [Fact]
    public void DefaultLogsRoot_UsesLocalAppDataPacToolkitsLogs()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PacToolkits",
            "logs");

        Assert.Equal(expected, AgentsLogPaths.DefaultLogsRoot());
    }

    [Fact]
    public void ResolveRoot_Empty_UsesDefault()
    {
        Assert.Equal(AgentsLogPaths.DefaultLogsRoot(), AgentsLogPaths.ResolveRoot(null));
        Assert.Equal(AgentsLogPaths.DefaultLogsRoot(), AgentsLogPaths.ResolveRoot("  "));
    }

    [Fact]
    public void ResolveRoot_CustomRoot_PreservesAbsolute()
    {
        var custom = Path.Combine(Path.GetTempPath(), "pac-logs-root");
        Assert.Equal(Path.GetFullPath(custom), AgentsLogPaths.ResolveRoot(custom));
    }

    [Fact]
    public void HostDir_IsUnderAgentsHost()
    {
        var expected = Path.Combine(AgentsLogPaths.DefaultLogsRoot(), "agents", "host");
        Assert.Equal(expected, AgentsLogPaths.HostDir());
    }

    [Fact]
    public void HostDir_CustomRoot_NestsUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pac-custom-logs");
        var expected = Path.Combine(Path.GetFullPath(root), "agents", "host");
        Assert.Equal(expected, AgentsLogPaths.HostDir(root));
    }

    [Fact]
    public void ModuleDir_UsesModuleIdSegment()
    {
        var expected = Path.Combine(AgentsLogPaths.DefaultLogsRoot(), "agents", "modules", "Injector");
        Assert.Equal(expected, AgentsLogPaths.ModuleDir("Injector"));
    }

    [Fact]
    public void ModuleDir_BlankId_UsesUnknown()
    {
        var expected = Path.Combine(AgentsLogPaths.DefaultLogsRoot(), "agents", "modules", "unknown");
        Assert.Equal(expected, AgentsLogPaths.ModuleDir("  "));
    }

    [Fact]
    public void DesktopDir_NestsUnderRoot()
    {
        Assert.Equal(
            Path.Combine(AgentsLogPaths.DefaultLogsRoot(), "desktop"),
            AgentsLogPaths.DesktopDir());
    }
}
