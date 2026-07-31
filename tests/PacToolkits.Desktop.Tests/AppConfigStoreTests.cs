using System.Text.Json.Nodes;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class AppConfigStoreTests
{
    private const string TestModuleId = "ModuleA";

    [Fact]
    public void InitConfig_creates_default_config_on_first_run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pactoolkits-config-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);

            var store = new AppConfigStore(dir);

            Assert.True(File.Exists(store.ConfigPath));
            Assert.Equal("PacToolkits.Desktop.config.json", Path.GetFileName(store.ConfigPath));

            var loaded = store.Load();
            Assert.Equal(2, loaded.SchemaVersion);
            Assert.Equal("localhost", loaded.Postgres.Host);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Normalize_sets_schema_version_2()
    {
        var normalized = AppConfigStore.Normalize(new AppConfigRoot { SchemaVersion = 0 });

        Assert.Equal(2, normalized.SchemaVersion);
    }

    [Fact]
    public void Keeps_modules_enabled_flag_when_scan_unavailable()
    {
        // 模块目录不可用时保留已有启用状态，避免部署窗口造成配置丢失
        var root = new AppConfigRoot
        {
            Agents = new AgentsOptions
            {
                ExecutablePath = @"C:\Missing\Agents\Agents.exe",
                ProcessName = "Agents",
                Modules =
                {
                    [TestModuleId] = new ModuleOptions { Enabled = false },
                    ["Probe"] = new ModuleOptions { Enabled = true },
                },
            },
        };

        var normalized = AppConfigStore.Normalize(root);

        Assert.True(normalized.Agents.Modules.TryGetValue(TestModuleId, out var module));
        Assert.False(module!.Enabled);
        Assert.True(normalized.Agents.Modules.TryGetValue("Probe", out var probe));
        Assert.True(probe!.Enabled);
    }

    [Fact]
    public void Does_not_seed_modules_when_catalog_is_empty_and_scan_unavailable()
    {
        var normalized = AppConfigStore.Normalize(new AppConfigRoot
        {
            Agents = new AgentsOptions
            {
                ExecutablePath = @"C:\Missing\Agents\Agents.exe",
                Modules = new Dictionary<string, ModuleOptions>(StringComparer.Ordinal),
            },
        });

        Assert.Empty(normalized.Agents.Modules);
    }

    [Fact]
    public void Keeps_module_flags_when_host_exists_but_catalog_is_temporarily_empty()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), "pactoolkits-appconfig-empty-" + Guid.NewGuid().ToString("N"));
        var agentsDir = Path.Combine(rootDir, "Agents");
        try
        {
            Directory.CreateDirectory(agentsDir);
            var hostPath = Path.Combine(agentsDir, AgentsPaths.HostExecutableFileName);
            File.WriteAllBytes(hostPath, [0]);

            var normalized = AppConfigStore.Normalize(new AppConfigRoot
            {
                Agents = new AgentsOptions
                {
                    ExecutablePath = hostPath,
                    Modules =
                    {
                        [TestModuleId] = new ModuleOptions { Enabled = false },
                    },
                },
            });

            Assert.False(normalized.Agents.Modules[TestModuleId].Enabled);
        }
        finally
        {
            if (Directory.Exists(rootDir))
            {
                Directory.Delete(rootDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Module_editor_preserves_settings_not_declared_by_schema()
    {
        var schema = new PacToolkits.Agents.Contracts.Settings.ModuleSettingsSchema
        {
            Sections =
            [
                new PacToolkits.Agents.Contracts.Settings.ModuleSettingsSection
                {
                    Fields =
                    [
                        new PacToolkits.Agents.Contracts.Settings.ModuleSettingsField
                        {
                            Key = "Known",
                            Type = PacToolkits.Agents.Contracts.Settings.ModuleSettingsFieldTypes.String,
                        },
                    ],
                },
            ],
        };
        var editor = new ModuleSettingsEditor(
            "Probe",
            "Probe",
            schema,
            new JsonObject { ["Known"] = "before", ["Future"] = 42 });
        editor.Sections[0].Fields[0].StringValue = "after";

        var saved = editor.ToJsonObject();

        Assert.Equal("after", saved["Known"]!.GetValue<string>());
        Assert.Equal(42, saved["Future"]!.GetValue<int>());
    }

    [Fact]
    public void Seeds_scanned_modules_enabled_and_drops_orphans()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), "pactoolkits-appconfig-" + Guid.NewGuid().ToString("N"));
        var agentsDir = Path.Combine(rootDir, "Agents");
        var modulesRoot = Path.Combine(agentsDir, AgentsPaths.ModulesDirectoryName);
        try
        {
            Directory.CreateDirectory(agentsDir);
            File.WriteAllBytes(Path.Combine(agentsDir, AgentsPaths.HostExecutableFileName), [0]);
            WriteModule(modulesRoot, "Alpha");
            WriteModule(modulesRoot, "Beta");

            var normalized = AppConfigStore.Normalize(new AppConfigRoot
            {
                Agents = new AgentsOptions
                {
                    ExecutablePath = Path.Combine(agentsDir, AgentsPaths.HostExecutableFileName),
                    Modules =
                    {
                        ["Alpha"] = new ModuleOptions { Enabled = false },
                        ["Gone"] = new ModuleOptions { Enabled = true },
                    },
                },
            });

            Assert.False(normalized.Agents.Modules.ContainsKey("Gone"));
            Assert.True(normalized.Agents.Modules.TryGetValue("Alpha", out var alpha));
            Assert.False(alpha!.Enabled);
            Assert.True(normalized.Agents.Modules.TryGetValue("Beta", out var beta));
            Assert.True(beta!.Enabled);
        }
        finally
        {
            try
            {
                if (Directory.Exists(rootDir))
                {
                    Directory.Delete(rootDir, recursive: true);
                }
            }
            catch
            {
                // 共享 CI 运行器可能延迟释放临时文件，清理失败不影响测试结论
            }
        }
    }

    private static void WriteModule(string modulesRoot, string id)
    {
        var moduleDir = Path.Combine(modulesRoot, id);
        Directory.CreateDirectory(moduleDir);
        File.WriteAllText(
            Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName),
            "{"
            + $"\"id\":\"{id}\","
            + "\"version\":\"1.0.0\","
            + "\"runtime\":\"ahk\","
            + $"\"displayName\":\"{id}\","
            + $"\"entry\":{{\"win-x64\":\"{id}.exe\"}},"
            + "\"desktop\":{"
            + "\"icons\":{\"active\":\"Puzzle\",\"inactive\":\"Box\"},"
            + "\"bottomStatusBar\":true,"
            + "\"topStatusPills\":true,"
            + "\"order\":10"
            + "},"
            + "\"package\":{\"builder\":\"ahk2exe\",\"ahk2exe\":{\"icon\":\"assets/x.ico\"}}"
            + "}");
    }
}
