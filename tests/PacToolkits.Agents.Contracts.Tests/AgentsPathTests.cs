using PacToolkits.Agents.Contracts.Agents;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class AgentsPathTests : IDisposable
{
    private readonly string _baseDirectory;

    public AgentsPathTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "pactoolkits-agents-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResolveHost_null_config_ignores_main_tools_on_disk()
    {
        var toolsDirectory = Path.Combine(_baseDirectory, "Tools");
        Directory.CreateDirectory(toolsDirectory);
        File.WriteAllText(Path.Combine(toolsDirectory, AgentsPaths.LegacyToolsFileName), string.Empty);

        var resolution = AgentsPath.ResolveHost(null, _baseDirectory);

        Assert.Equal(HostExecutableResolutionSource.Missing, resolution.Source);
        Assert.Null(resolution.ResolvedPath);
        Assert.Equal(AgentsPaths.HostExecutable, resolution.StoredPath);
    }

    [Fact]
    public void ResolveHost_standard_exists()
    {
        CreateHostExecutable();

        var resolution = AgentsPath.ResolveHost(null, _baseDirectory);

        Assert.Equal(AgentsPaths.HostExecutable, resolution.StoredPath);
        Assert.NotNull(resolution.ResolvedPath);
        Assert.True(File.Exists(resolution.ResolvedPath));
        Assert.EndsWith(AgentsPaths.HostExecutableFileName, resolution.ResolvedPath);
        Assert.Equal(HostExecutableResolutionSource.Standard, resolution.Source);
    }

    [Fact]
    public void ResolveHost_main_tools_config_rewrites_to_host()
    {
        CreateHostExecutable();
        CreateLegacyToolsExecutable();

        var resolution = AgentsPath.ResolveHost(
            AgentsPaths.LegacyToolsExecutable,
            _baseDirectory);

        Assert.Equal(HostExecutableResolutionSource.Standard, resolution.Source);
        Assert.Equal(AgentsPaths.HostExecutable, resolution.StoredPath);
        Assert.NotNull(resolution.ResolvedPath);
        Assert.EndsWith(AgentsPaths.HostExecutableFileName, resolution.ResolvedPath);
    }

    [Fact]
    public void ResolveHost_main_tools_rewrites_without_host_binary()
    {
        CreateLegacyToolsExecutable();

        var resolution = AgentsPath.ResolveHost(
            AgentsPaths.LegacyToolsExecutable,
            _baseDirectory);

        Assert.Equal(HostExecutableResolutionSource.Missing, resolution.Source);
        Assert.Equal(AgentsPaths.HostExecutable, resolution.StoredPath);
        Assert.Null(resolution.ResolvedPath);
    }

    [Fact]
    public void ResolveHost_missing_custom_falls_back_to_standard()
    {
        CreateHostExecutable();

        var resolution = AgentsPath.ResolveHost(
            @".\Custom\missing-host.exe",
            _baseDirectory);

        Assert.Equal(HostExecutableResolutionSource.Standard, resolution.Source);
        Assert.Equal(AgentsPaths.HostExecutable, resolution.StoredPath);
        Assert.NotNull(resolution.ResolvedPath);
    }

    [Fact]
    public void ResolveHost_custom_path()
    {
        CreateHostExecutable();
        var customDirectory = Path.Combine(_baseDirectory, "Custom");
        Directory.CreateDirectory(customDirectory);
        var customExecutable = Path.Combine(customDirectory, "custom-host.exe");
        File.WriteAllText(customExecutable, string.Empty);
        var configured = @".\Custom\custom-host.exe";

        var resolution = AgentsPath.ResolveHost(configured, _baseDirectory);

        Assert.Equal(HostExecutableResolutionSource.Configured, resolution.Source);
        Assert.Equal(configured, resolution.StoredPath);
    }

    [Theory]
    [InlineData(@".\Tools\pacinjector.exe")]
    [InlineData(@"Tools/pacinjector.exe")]
    [InlineData(@"C:\Apps\PacToolkits\Tools\pacinjector.exe")]
    public void IsLegacyTools_accepts(string storedPath)
    {
        Assert.True(AgentsPath.IsLegacyToolsStoredPath(storedPath));
    }

    [Theory]
    [InlineData(@".\Agents\Agents.exe")]
    [InlineData(@".\Agents\Modules\Injector\Injector.exe")]
    [InlineData(@"D:\Other\pacinjector.exe")]
    public void IsLegacyTools_rejects(string storedPath)
    {
        Assert.False(AgentsPath.IsLegacyToolsStoredPath(storedPath));
    }

    [Fact]
    public void TryResolveModuleEntry_reads_windows_x64()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, AgentsPaths.InjectorModuleId);
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(
            Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName),
            """
            {"id":"Injector","version":"1.2.3","entry":{"windows-x64":"Injector.exe"}}
            """);

        var entry = AgentsPath.TryResolveModuleEntryPath(agentsDir, AgentsPaths.InjectorModuleId);
        var version = AgentsPath.TryReadModuleVersion(
            AgentsPaths.ModuleManifestPath(agentsDir, AgentsPaths.InjectorModuleId));

        Assert.Equal(Path.Combine(moduleDir, "Injector.exe"), entry);
        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public void TryReadHostVersion_reads_agents_component_version()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        Directory.CreateDirectory(agentsDir);
        File.WriteAllText(
            Path.Combine(agentsDir, "ReleaseManifest.json"),
            """
            {"components":{"agents":{"version":"0.1.1","modules":{"Injector":{"version":"0.7.0"}}}}}
            """);

        var version = AgentsPath.TryReadHostVersion(Path.Combine(agentsDir, "ReleaseManifest.json"));

        Assert.Equal("0.1.1", version);
    }

    private void CreateHostExecutable()
    {
        var standardDirectory = Path.Combine(_baseDirectory, "Agents");
        Directory.CreateDirectory(standardDirectory);
        File.WriteAllText(Path.Combine(standardDirectory, AgentsPaths.HostExecutableFileName), string.Empty);
    }

    private void CreateLegacyToolsExecutable()
    {
        var toolsDirectory = Path.Combine(_baseDirectory, "Tools");
        Directory.CreateDirectory(toolsDirectory);
        File.WriteAllText(Path.Combine(toolsDirectory, AgentsPaths.LegacyToolsFileName), string.Empty);
    }
}
