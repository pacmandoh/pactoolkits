using PacToolkits.Agent.Contracts.Agents;
using Xunit;

namespace PacToolkits.Agent.Contracts.Tests;

public sealed class AgentPathResolverTests : IDisposable
{
    private readonly string _baseDirectory;

    public AgentPathResolverTests()
    {
        _baseDirectory = Path.Combine(Path.GetTempPath(), "pactoolkits-agent-path-" + Guid.NewGuid().ToString("N"));
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
    public void Resolve_legacy_only_missing()
    {
        var legacyDirectory = Path.Combine(_baseDirectory, "Tools");
        Directory.CreateDirectory(legacyDirectory);
        var legacyExecutable = Path.Combine(legacyDirectory, AgentPaths.LegacyInjectorFileName);
        File.WriteAllText(legacyExecutable, string.Empty);

        var resolution = AgentPathResolver.ResolveInjectorAhk(null, _baseDirectory);

        Assert.Equal(AgentExecutableResolutionSource.Missing, resolution.Source);
        Assert.Null(resolution.ResolvedPath);
        Assert.Equal(AgentPaths.InjectorAhkExecutable, resolution.StoredPath);
        Assert.False(resolution.RequiresMigration);
    }

    [Fact]
    public void Resolve_standard_exists()
    {
        CreateStandardExecutable();

        var resolution = AgentPathResolver.ResolveInjectorAhk(null, _baseDirectory);

        Assert.Equal(AgentPaths.InjectorAhkExecutable, resolution.StoredPath);
        Assert.NotNull(resolution.ResolvedPath);
        Assert.True(File.Exists(resolution.ResolvedPath));
        Assert.EndsWith(AgentPaths.InjectorAhkExecutableFileName, resolution.ResolvedPath);
        Assert.Equal(AgentExecutableResolutionSource.Standard, resolution.Source);
    }

    [Fact]
    public void Resolve_legacy_config_migrates()
    {
        CreateStandardExecutable();
        CreateLegacyExecutable();

        var resolution = AgentPathResolver.ResolveInjectorAhk(
            AgentPaths.LegacyInjectorExecutable,
            _baseDirectory);

        Assert.Equal(AgentExecutableResolutionSource.Standard, resolution.Source);
        Assert.Equal(AgentPaths.InjectorAhkExecutable, resolution.StoredPath);
        Assert.True(resolution.RequiresMigration);
        Assert.NotNull(resolution.ResolvedPath);
        Assert.EndsWith(AgentPaths.InjectorAhkExecutableFileName, resolution.ResolvedPath);
    }

    [Fact]
    public void Resolve_previous_standard_migrates()
    {
        CreateStandardExecutable();

        var resolution = AgentPathResolver.ResolveInjectorAhk(
            AgentPaths.PreviousInjectorAhkExecutable,
            _baseDirectory);

        Assert.Equal(AgentExecutableResolutionSource.Standard, resolution.Source);
        Assert.Equal(AgentPaths.InjectorAhkExecutable, resolution.StoredPath);
        Assert.True(resolution.RequiresMigration);
    }

    [Fact]
    public void Resolve_legacy_missing_migrates()
    {
        CreateStandardExecutable();

        var resolution = AgentPathResolver.ResolveInjectorAhk(
            AgentPaths.LegacyInjectorExecutable,
            _baseDirectory);

        Assert.Equal(AgentExecutableResolutionSource.Standard, resolution.Source);
        Assert.Equal(AgentPaths.InjectorAhkExecutable, resolution.StoredPath);
        Assert.True(resolution.RequiresMigration);
    }

    [Fact]
    public void Resolve_custom_path()
    {
        CreateStandardExecutable();
        var customDirectory = Path.Combine(_baseDirectory, "Custom");
        Directory.CreateDirectory(customDirectory);
        var customExecutable = Path.Combine(customDirectory, "custom-agent.exe");
        File.WriteAllText(customExecutable, string.Empty);
        var configured = @".\Custom\custom-agent.exe";

        var resolution = AgentPathResolver.ResolveInjectorAhk(configured, _baseDirectory);

        Assert.Equal(AgentExecutableResolutionSource.Configured, resolution.Source);
        Assert.Equal(configured, resolution.StoredPath);
        Assert.False(resolution.RequiresMigration);
    }

    [Theory]
    [InlineData(@".\Tools\pacinjector.exe")]
    [InlineData(@"Tools/pacinjector.exe")]
    [InlineData(@"C:\Apps\PacToolkits\Tools\pacinjector.exe")]
    public void IsLegacy_accepts(string storedPath)
    {
        Assert.True(AgentPathResolver.IsLegacyInjectorStoredPath(storedPath));
    }

    [Theory]
    [InlineData(@".\Agents\injector\pactoolkits-injector.exe")]
    [InlineData(@"D:\Other\pacinjector.exe")]
    public void IsLegacy_rejects(string storedPath)
    {
        Assert.False(AgentPathResolver.IsLegacyInjectorStoredPath(storedPath));
    }

    [Theory]
    [InlineData(@".\Agents\agent-injector-ahk\pactoolkits-agent-injector-ahk.exe")]
    [InlineData(@"Agents/agent-injector-ahk/pactoolkits-agent-injector-ahk.exe")]
    public void IsPreviousStandard_accepts(string storedPath)
    {
        Assert.True(AgentPathResolver.IsPreviousStandardInjectorStoredPath(storedPath));
    }

    private void CreateStandardExecutable()
    {
        var standardDirectory = Path.Combine(_baseDirectory, "Agents", "injector");
        Directory.CreateDirectory(standardDirectory);
        var standardExecutable = Path.Combine(standardDirectory, AgentPaths.InjectorAhkExecutableFileName);
        File.WriteAllText(standardExecutable, string.Empty);
    }

    private void CreateLegacyExecutable()
    {
        var legacyDirectory = Path.Combine(_baseDirectory, "Tools");
        Directory.CreateDirectory(legacyDirectory);
        var legacyExecutable = Path.Combine(legacyDirectory, AgentPaths.LegacyInjectorFileName);
        File.WriteAllText(legacyExecutable, string.Empty);
    }
}
