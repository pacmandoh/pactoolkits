using System.Text.Json;
using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Agents.Contracts.Models;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Tests;

public sealed class AppConfigStoreTests
{
    [Fact]
    public void Normalize_sets_schema_version_2()
    {
        var normalized = AppConfigStore.Normalize(new AppConfigRoot { SchemaVersion = 1 });

        Assert.Equal(2, normalized.SchemaVersion);
        Assert.NotNull(normalized.Agents.Injector);
        Assert.True(normalized.Agents.Injector.Enabled);
    }

    [Fact]
    public void Keeps_injector_disabled_flag()
    {
        var root = new AppConfigRoot
        {
            Agents = new AgentsOptions
            {
                ExecutablePath = @"C:\Apps\Agents\Agents.exe",
                ProcessName = "Agents",
                Injector = new InjectorOptions { Enabled = false },
            },
        };

        var normalized = AppConfigStore.Normalize(root);

        Assert.False(normalized.Agents.Injector.Enabled);
    }

    [Fact]
    public void Migrates_main_tools_host_path()
    {
        var root = new AppConfigRoot
        {
            Agents = new AgentsOptions
            {
                ExecutablePath = AgentsPaths.LegacyToolsExecutable,
                ProcessName = AgentsPaths.LegacyToolsProcessName,
            },
        };

        var normalized = AppConfigStore.Normalize(root);

        Assert.Equal(AgentsPaths.HostExecutable, normalized.Agents.ExecutablePath);
        Assert.Equal(AgentsPaths.HostProcessName, normalized.Agents.ProcessName);
    }

    [Fact]
    public void Migrates_v1_json_to_v2_from_main_automation_tools()
    {
        var v1 = """
                 {
                   "SchemaVersion": 1,
                   "AutomationTools": {
                     "Ahk": {
                       "ExecutablePath": ".\\Tools\\pacinjector.exe",
                       "ProcessName": "pacinjector"
                     },
                     "Agent": {
                       "Enabled": true,
                       "PgDriver": "{PostgreSQL ODBC Driver}",
                       "PgSsl": "require"
                     }
                   },
                   "Agents": {
                     "agents": {
                       "Enabled": false,
                       "ExecutablePath": ".\\Agents\\Agents.exe",
                       "ProcessName": "Agents",
                       "Settings": {
                         "PgDriver": "Stale Companion Driver",
                         "PgSsl": "disable"
                       }
                     }
                   },
                   "MsfxApi": { "RefEntId": "x" },
                   "Postgres": { "Host": "localhost", "Port": 5432, "Database": "db", "Username": "u" }
                 }
                 """;

        var normalized = MigrateAndNormalize(v1);

        Assert.Equal(2, normalized.SchemaVersion);
        Assert.Equal(AgentsPaths.HostExecutable, normalized.Agents.ExecutablePath);
        Assert.Equal(AgentsPaths.HostProcessName, normalized.Agents.ProcessName);
        Assert.True(normalized.Agents.Injector.Enabled);
        Assert.Equal("{PostgreSQL ODBC Driver}", normalized.Agents.Injector.PgDriver);
        Assert.Equal("require", normalized.Agents.Injector.PgSsl);

        var migratedJson = AppConfigStore.MigrateConfigJsonToV2(v1);
        using var doc = JsonDocument.Parse(migratedJson);
        Assert.False(doc.RootElement.TryGetProperty("AutomationTools", out _));
        var agents = doc.RootElement.GetProperty("Agents");
        Assert.False(agents.TryGetProperty("Enabled", out _));
        Assert.False(agents.TryGetProperty("agents", out _));
        Assert.True(agents.GetProperty("Injector").GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public void Already_v2_json_stays_single_tree()
    {
        var v2 = """
                 {
                   "SchemaVersion": 2,
                   "Agents": {
                     "ExecutablePath": ".\\Agents\\Agents.exe",
                     "ProcessName": "Agents",
                     "Enabled": false,
                     "Injector": {
                       "Enabled": true,
                       "PgDriver": "PostgreSQL Unicode(x64)",
                       "PgSsl": "disable"
                     }
                   }
                 }
                 """;

        var migrated = AppConfigStore.MigrateConfigJsonToV2(v2);
        using var doc = JsonDocument.Parse(migrated);
        Assert.Equal(2, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("AutomationTools", out _));
        var agents = doc.RootElement.GetProperty("Agents");
        Assert.False(agents.TryGetProperty("Enabled", out _));
        Assert.False(agents.TryGetProperty("agents", out _));
        Assert.True(agents.GetProperty("Injector").GetProperty("Enabled").GetBoolean());
        Assert.Equal(
            "PostgreSQL Unicode(x64)",
            agents.GetProperty("Injector").GetProperty("PgDriver").GetString());
    }

    private static AppConfigRoot MigrateAndNormalize(string json)
    {
        var migrated = AppConfigStore.MigrateConfigJsonToV2(json);
        var root = JsonSerializer.Deserialize<AppConfigRoot>(migrated) ?? new AppConfigRoot();
        return AppConfigStore.Normalize(root);
    }
}
