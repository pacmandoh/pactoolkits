using PacToolkits.Agents.Contracts.Agents;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class AgentsPathTests : IDisposable
{
    private const string TestModuleId = "Sample";
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
    public void ResolveHost_null_config_is_missing_without_standard_binary()
    {
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

    [Fact]
    public void TryResolveModuleEntry_reads_windows_x64()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, TestModuleId);
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(
            Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName),
            """
            {"id":"Sample","version":"1.2.3","entry":{"win-x64":"Sample.exe"}}
            """);

        var entry = AgentsPath.TryResolveModuleEntryPath(agentsDir, TestModuleId);
        var version = AgentsPath.TryReadModuleVersion(
            AgentsPaths.ModuleManifestPath(agentsDir, TestModuleId));

        Assert.Equal(Path.Combine(moduleDir, "Sample.exe"), entry);
        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public void TryReadModule_requires_desktop_dual_icons_and_ahk2exe_package()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, TestModuleId);
        Directory.CreateDirectory(moduleDir);
        var assetsDir = Path.Combine(moduleDir, "assets");
        Directory.CreateDirectory(assetsDir);
        var iconPath = Path.Combine(assetsDir, "agents-sample.ico");
        File.WriteAllText(iconPath, string.Empty);
        var manifestPath = Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName);
        File.WriteAllText(manifestPath, FullModuleManifestJson());

        var module = AgentsPath.TryReadModule(manifestPath);

        Assert.NotNull(module);
        Assert.Equal(TestModuleId, module.Id);
        Assert.Equal(ModuleRuntimes.Ahk, module.Runtime);
        Assert.Equal("追溯码录入", module.DisplayName);
        Assert.Equal("Bone", module.Desktop.Icons.Active);
        Assert.Equal("BoneFracture", module.Desktop.Icons.Inactive);
        Assert.True(module.Desktop.BottomStatusBar);
        Assert.True(module.Desktop.TopStatusPills);
        Assert.Equal(10, module.Desktop.Order);
        Assert.True(module.RequiresApiContract);
        Assert.Equal("1.2.25", module.MinApiContract);
        Assert.Equal("1.2.25", module.MaxApiContract);
        Assert.Equal("Sample.exe", module.EntryWinX64);
        Assert.Equal(ModuleBuilders.Ahk2Exe, module.Package.Builder);
        Assert.Equal("assets/agents-sample.ico", module.Package.Ahk2Exe.Icon);
        Assert.Equal(
            Path.GetFullPath(iconPath),
            AgentsPath.TryResolveAhk2ExeIconPath(module));
    }

    [Fact]
    public void TryReadModule_omitted_api_contract_means_no_protocol_gate()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, "NoDb");
        Directory.CreateDirectory(moduleDir);
        var assetsDir = Path.Combine(moduleDir, "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "ico.ico"), string.Empty);
        var manifestPath = Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName);
        File.WriteAllText(manifestPath, FullManifestJson("NoDb", "NoDb.exe", order: 1, icon: "assets/ico.ico"));

        var module = AgentsPath.TryReadModule(manifestPath);

        Assert.NotNull(module);
        Assert.False(module.RequiresApiContract);
        Assert.Null(module.MinApiContract);
        Assert.Null(module.MaxApiContract);
    }

    [Fact]
    public void TryReadModule_parses_api_contract_range()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, "WithDb");
        Directory.CreateDirectory(moduleDir);
        var assetsDir = Path.Combine(moduleDir, "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "ico.ico"), string.Empty);
        var manifestPath = Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName);
        File.WriteAllText(
            manifestPath,
            FullManifestJson(
                "WithDb",
                "WithDb.exe",
                order: 2,
                icon: "assets/ico.ico",
                minApiContract: "1.2.20",
                maxApiContract: "1.2.25"));

        var module = AgentsPath.TryReadModule(manifestPath);

        Assert.NotNull(module);
        Assert.True(module.RequiresApiContract);
        Assert.Equal("1.2.20", module.MinApiContract);
        Assert.Equal("1.2.25", module.MaxApiContract);
    }

    [Fact]
    public void TryReadModule_rejects_partial_api_contract_range()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, "PartialDb");
        Directory.CreateDirectory(moduleDir);
        var assetsDir = Path.Combine(moduleDir, "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "ico.ico"), string.Empty);
        var manifestPath = Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName);
        File.WriteAllText(
            manifestPath,
            FullManifestJson(
                "PartialDb",
                "PartialDb.exe",
                order: 3,
                icon: "assets/ico.ico",
                minApiContract: "1.2.20",
                maxApiContract: null));

        Assert.Null(AgentsPath.TryReadModule(manifestPath));
    }

    [Fact]
    public void TryReadModule_rejects_inverted_api_contract_range()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, "BadRange");
        Directory.CreateDirectory(moduleDir);
        var assetsDir = Path.Combine(moduleDir, "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "ico.ico"), string.Empty);
        var manifestPath = Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName);
        File.WriteAllText(
            manifestPath,
            FullManifestJson(
                "BadRange",
                "BadRange.exe",
                order: 4,
                icon: "assets/ico.ico",
                minApiContract: "1.2.25",
                maxApiContract: "1.2.20"));

        Assert.Null(AgentsPath.TryReadModule(manifestPath));
    }

    [Fact]
    public void TryReadModule_parses_required_api_scopes()
    {
        var manifestPath = WriteFullModule(
            "Scoped",
            extraFields: ",\"requiredApiScopes\":[\"read\",\"write\"]");

        var module = AgentsPath.TryReadModule(manifestPath);

        Assert.NotNull(module);
        Assert.Equal(["read", "write"], module.RequiredApiScopes);
    }

    [Fact]
    public void TryReadModule_accepts_empty_required_api_scopes_array()
    {
        var manifestPath = WriteFullModule("EmptyScopes", extraFields: ",\"requiredApiScopes\":[]");

        var module = AgentsPath.TryReadModule(manifestPath);

        Assert.NotNull(module);
        Assert.Null(module.RequiredApiScopes);
    }

    [Theory]
    [InlineData(",\"requiredApiScopes\":\"read\"")]
    [InlineData(",\"requiredApiScopes\":[1]")]
    [InlineData(",\"requiredApiScopes\":[\"read\",1]")]
    [InlineData(",\"requiredApiScopes\":[\"read\",\"\"]")]
    [InlineData(",\"requiredApiScopes\":[\"read\",\"  \"]")]
    public void TryReadModule_rejects_illegal_required_api_scopes(string extraFields)
    {
        var manifestPath = WriteFullModule("BadScopes", extraFields: extraFields);

        Assert.Null(AgentsPath.TryReadModule(manifestPath));
    }

    [Fact]
    public void TryReadModule_rejects_missing_required_desktop_or_package_fields()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, "Plain");
        Directory.CreateDirectory(moduleDir);
        var manifestPath = Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName);
        File.WriteAllText(
            manifestPath,
            """
            {"id":"Plain","version":"1.0.0","runtime":"ahk","displayName":"Plain","entry":{"win-x64":"Plain.exe"}}
            """);

        Assert.Null(AgentsPath.TryReadModule(manifestPath));
    }

    [Fact]
    public void Module_paths_reject_parent_traversal()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, TestModuleId);
        Directory.CreateDirectory(moduleDir);
        var manifestPath = Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName);
        File.WriteAllText(
            manifestPath,
            FullManifestJson(
                TestModuleId,
                "../Outside.exe",
                order: 10));

        Assert.Null(AgentsPath.TryReadModule(manifestPath));
        Assert.Null(AgentsPath.TryResolveModuleEntryPath(agentsDir, TestModuleId));

        File.WriteAllText(
            manifestPath,
            FullManifestJson(
                TestModuleId,
                "Sample.exe",
                order: 10,
                icon: "../outside.ico"));

        Assert.Null(AgentsPath.TryReadModule(manifestPath));
    }

    [Theory]
    [InlineData("../Probe")]
    [InlineData("Probe/Child")]
    [InlineData(" Probe")]
    [InlineData("探针")]
    public void Module_id_rejects_non_portable_path_tokens(string moduleId)
    {
        Assert.False(AgentsPath.IsValidModuleId(moduleId));
    }

    [Fact]
    public void ScanModules_orders_by_desktop_order_and_skips_id_mismatch()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var modulesRoot = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName);
        WriteModule(modulesRoot, "Beta", FullManifestJson("Beta", "Beta.exe", order: 20));
        WriteModule(modulesRoot, "Alpha", FullManifestJson("Alpha", "Alpha.exe", order: 5));
        WriteModule(modulesRoot, "Wrong", FullManifestJson("Other", "Wrong.exe", order: 1));
        WriteModule(modulesRoot, "Case", FullManifestJson("case", "Case.exe", order: 2));
        WriteModule(modulesRoot, "Broken", """{"id":"Broken"}""");

        var scanned = AgentsPath.ScanModules(agentsDir);

        Assert.Equal(["Alpha", "Beta"], scanned.Select(m => m.Id).ToArray());
        Assert.Equal([5, 20], scanned.Select(m => m.Desktop.Order).ToArray());
    }

    [Fact]
    public void CatalogEquals_detects_add_and_version_change()
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var modulesRoot = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName);
        WriteModule(modulesRoot, "Alpha", FullManifestJson("Alpha", "Alpha.exe", order: 5));

        var first = AgentsPath.ScanModules(agentsDir);
        Assert.True(AgentsPath.CatalogEquals(first, AgentsPath.ScanModules(agentsDir)));

        WriteModule(modulesRoot, "Beta", FullManifestJson("Beta", "Beta.exe", order: 20));
        var withBeta = AgentsPath.ScanModules(agentsDir);
        Assert.False(AgentsPath.CatalogEquals(first, withBeta));

        WriteModule(modulesRoot, "Alpha", FullManifestJson("Alpha", "Alpha.exe", order: 5, version: "9.9.9"));
        var bumped = AgentsPath.ScanModules(agentsDir);
        Assert.False(AgentsPath.CatalogEquals(withBeta, bumped));
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

    [Fact]
    public void TryReadAgentsDesktopBounds_reads_min_max_desktop()
    {
        var agentsDir = Path.Combine(_baseDirectory, "AgentsBounds");
        Directory.CreateDirectory(agentsDir);
        var manifestPath = Path.Combine(agentsDir, "ReleaseManifest.json");
        File.WriteAllText(
            manifestPath,
            """
            {"components":{"agents":{"version":"0.2.0","minDesktop":"1.0.0","maxDesktop":"1.1.0"}}}
            """);

        Assert.True(AgentsPath.TryReadAgentsDesktopBounds(manifestPath, out var min, out var max));
        Assert.Equal("1.0.0", min);
        Assert.Equal("1.1.0", max);
    }

    private string WriteFullModule(string id, string extraFields = "")
    {
        var agentsDir = Path.Combine(_baseDirectory, "Agents");
        var moduleDir = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName, id);
        Directory.CreateDirectory(moduleDir);
        var assetsDir = Path.Combine(moduleDir, "assets");
        Directory.CreateDirectory(assetsDir);
        File.WriteAllText(Path.Combine(assetsDir, "ico.ico"), string.Empty);
        var manifestPath = Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName);
        File.WriteAllText(
            manifestPath,
            FullManifestJson(id, id + ".exe", order: 1, icon: "assets/ico.ico", extraFields: extraFields));
        return manifestPath;
    }

    private static string FullModuleManifestJson()
        => FullManifestJson(
            TestModuleId,
            "Sample.exe",
            order: 10,
            displayName: "追溯码录入",
            active: "Bone",
            inactive: "BoneFracture",
            icon: "assets/agents-sample.ico",
            minApiContract: "1.2.25",
            maxApiContract: "1.2.25");

    private static string FullManifestJson(
        string id,
        string entry,
        int order,
        string? displayName = null,
        string active = "Puzzle",
        string inactive = "Box",
        string version = "1.0.0",
        string icon = "assets/agents-injector.ico",
        string? minApiContract = null,
        string? maxApiContract = null,
        string extraFields = "")
    {
        var name = displayName ?? id;
        var contractFields = string.Empty;
        if (minApiContract is not null && maxApiContract is null)
        {
            contractFields = $",\"minApiContract\":\"{minApiContract}\"";
        }
        else if (minApiContract is null && maxApiContract is not null)
        {
            contractFields = $",\"maxApiContract\":\"{maxApiContract}\"";
        }
        else if (minApiContract is not null && maxApiContract is not null)
        {
            contractFields = $",\"minApiContract\":\"{minApiContract}\",\"maxApiContract\":\"{maxApiContract}\"";
        }

        return
            "{"
            + $"\"id\":\"{id}\","
            + $"\"version\":\"{version}\","
            + "\"runtime\":\"ahk\","
            + $"\"displayName\":\"{name}\","
            + $"\"entry\":{{\"win-x64\":\"{entry}\"}}"
            + contractFields
            + extraFields
            + ","
            + "\"desktop\":{"
            + $"\"icons\":{{\"active\":\"{active}\",\"inactive\":\"{inactive}\"}},"
            + "\"bottomStatusBar\":true,"
            + "\"topStatusPills\":true,"
            + $"\"order\":{order}"
            + "},"
            + $"\"package\":{{\"builder\":\"ahk2exe\",\"ahk2exe\":{{\"icon\":\"{icon}\"}}}}"
            + "}";
    }


    private static void WriteModule(string modulesRoot, string folderName, string json)
    {
        var moduleDir = Path.Combine(modulesRoot, folderName);
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName), json);
    }

    private void CreateHostExecutable()
    {
        var standardDirectory = Path.Combine(_baseDirectory, "Agents");
        Directory.CreateDirectory(standardDirectory);
        File.WriteAllText(Path.Combine(standardDirectory, AgentsPaths.HostExecutableFileName), string.Empty);
    }
}
