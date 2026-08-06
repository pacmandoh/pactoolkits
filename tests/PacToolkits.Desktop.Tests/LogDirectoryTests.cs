using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Tests;

public sealed class LogDirectoryTests
{
    [Fact]
    public void Resolve_EmptyConfigured_UsesDefaultLayout()
    {
        var resolution = LogDirectory.Resolve(string.Empty);

        Assert.Equal(string.Empty, resolution.StoredDirectory);
        Assert.Equal(AgentsLogPaths.DesktopDir(null), resolution.RuntimeDirectory);
        Assert.Equal(AgentsLogPaths.DefaultLogsRoot(), resolution.BrowseDirectory);
    }

    [Fact]
    public void Resolve_CustomRoot_DesktopWritesUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pac-ui-logs-root");

        var resolution = LogDirectory.Resolve(root);

        Assert.Equal(Path.GetFullPath(root), resolution.StoredDirectory);
        Assert.Equal(AgentsLogPaths.DesktopDir(root), resolution.RuntimeDirectory);
        Assert.Equal(Path.GetFullPath(root), resolution.BrowseDirectory);
    }

    [Fact]
    public void Resolve_DefaultLogsRootPath_StoresEmpty()
    {
        var resolution = LogDirectory.Resolve(AgentsLogPaths.DefaultLogsRoot());

        Assert.Equal(string.Empty, resolution.StoredDirectory);
        Assert.Equal(AgentsLogPaths.DesktopDir(null), resolution.RuntimeDirectory);
        Assert.Equal(AgentsLogPaths.DefaultLogsRoot(), resolution.BrowseDirectory);
    }
}
