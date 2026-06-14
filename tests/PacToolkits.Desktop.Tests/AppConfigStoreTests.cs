using PacToolkits.Agent.Contracts.Agents;
using PacToolkits.Agent.Contracts.Models;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Tests;

public sealed class AppConfigStoreTests
{
    public sealed class Normalize
    {
        [Fact]
        public void Keeps_disabled_flag()
        {
            var root = new AppConfigRoot
            {
                AutomationTools =
                {
                    Ahk = new AhkToolOptions
                    {
                        ExecutablePath = @"C:\Apps\Agents\injector\pactoolkits-injector.exe",
                        ProcessName = "pactoolkits-injector",
                    },
                },
                Agents =
                {
                    [AgentIds.InjectorAhk] = new AgentInstanceConfig
                    {
                        Enabled = false,
                        ExecutablePath = @"C:\Apps\Agents\injector\pactoolkits-injector.exe",
                        ProcessName = "pactoolkits-injector",
                    },
                },
            };

            var normalized = AppConfigStore.Normalize(root);

            Assert.False(normalized.Agents[AgentIds.InjectorAhk].Enabled);
        }

        [Fact]
        public void Migrates_legacy_settings()
        {
            var root = new AppConfigRoot
            {
                AutomationTools =
                {
                    Ahk = new AhkToolOptions
                    {
                        ExecutablePath = @"C:\Apps\Agents\injector\pactoolkits-injector.exe",
                        ProcessName = "pactoolkits-injector",
                    },
                    Agent = new AgentToolOptions
                    {
                        PgDriver = "{PostgreSQL ODBC Driver}",
                        PgSsl = "require",
                    },
                },
            };

            var normalized = AppConfigStore.Normalize(root);
            var agent = normalized.Agents[AgentIds.InjectorAhk];

            Assert.NotEmpty(agent.Settings);
            Assert.Equal("{PostgreSQL ODBC Driver}", normalized.AutomationTools.Agent.PgDriver);
            Assert.Equal("require", normalized.AutomationTools.Agent.PgSsl);
        }

        [Fact]
        public void Tools_save_wins()
        {
            var legacyAgent = new AgentToolOptions
            {
                PgDriver = "Legacy Driver",
                PgSsl = "disable",
            };
            var savedAgent = new AgentToolOptions
            {
                PgDriver = "{PostgreSQL ODBC Driver}",
                PgSsl = "require",
            };
            var root = new AppConfigRoot
            {
                AutomationTools =
                {
                    Ahk = new AhkToolOptions
                    {
                        ExecutablePath = @"D:\Agents\pactoolkits-injector.exe",
                        ProcessName = "pactoolkits-injector",
                    },
                    Agent = savedAgent,
                },
                Agents =
                {
                    [AgentIds.InjectorAhk] = new AgentInstanceConfig
                    {
                        ExecutablePath = @"C:\Legacy\pacinjector.exe",
                        ProcessName = "pacinjector",
                        Settings = AgentSettingsSync.ToSettings(legacyAgent),
                    },
                },
            };

            AppConfigStore.SyncInjectorFromTools(root, root.AutomationTools);

            var normalized = AppConfigStore.Normalize(root);
            var agent = normalized.Agents[AgentIds.InjectorAhk];

            Assert.Equal("{PostgreSQL ODBC Driver}", normalized.AutomationTools.Agent.PgDriver);
            Assert.Equal("require", normalized.AutomationTools.Agent.PgSsl);
            Assert.Equal("{PostgreSQL ODBC Driver}", AgentSettingsSync.FromSettings(agent.Settings!).PgDriver);
            Assert.Equal(@"D:\Agents\pactoolkits-injector.exe", agent.ExecutablePath);
            Assert.Equal("pactoolkits-injector", agent.ProcessName);
        }

        [Fact]
        public void Tools_path_wins_on_save()
        {
            var root = new AppConfigRoot
            {
                AutomationTools =
                {
                    Ahk = new AhkToolOptions
                    {
                        ExecutablePath = AgentPaths.InjectorAhkExecutable,
                        ProcessName = "pactoolkits-injector",
                    },
                    Agent = new AgentToolOptions
                    {
                        PgDriver = "{PostgreSQL ODBC Driver}",
                        PgSsl = "require",
                    },
                },
                Agents =
                {
                    [AgentIds.InjectorAhk] = new AgentInstanceConfig
                    {
                        ExecutablePath = @"C:\Apps\Agents\injector\pactoolkits-injector.exe",
                        ProcessName = "pactoolkits-injector",
                        Settings = AgentSettingsSync.ToSettings(new AgentToolOptions
                        {
                            PgDriver = "{PostgreSQL ODBC Driver}",
                            PgSsl = "require",
                        }),
                    },
                },
            };

            AppConfigStore.SyncInjectorFromTools(root, root.AutomationTools);

            var normalized = AppConfigStore.Normalize(root);
            var agent = normalized.Agents[AgentIds.InjectorAhk];

            Assert.Equal(AgentPaths.InjectorAhkExecutable, agent.ExecutablePath);
            Assert.Equal(AgentPaths.InjectorAhkExecutable, normalized.AutomationTools.Ahk.ExecutablePath);
        }

        [Fact]
        public void Prefers_agent_settings()
        {
            var agentsSettings = new AgentToolOptions
            {
                PgDriver = "Agents Driver",
                PgSsl = "require",
            };
            var root = new AppConfigRoot
            {
                AutomationTools =
                {
                    Ahk = new AhkToolOptions
                    {
                        ExecutablePath = AgentPaths.InjectorAhkExecutable,
                        ProcessName = "pactoolkits-injector",
                    },
                    Agent = new AgentToolOptions
                    {
                        PgDriver = "Legacy Automation Driver",
                        PgSsl = "disable",
                    },
                },
                Agents =
                {
                    [AgentIds.InjectorAhk] = new AgentInstanceConfig
                    {
                        ExecutablePath = @"C:\Custom\agent.exe",
                        ProcessName = "custom-agent",
                        Settings = AgentSettingsSync.ToSettings(agentsSettings),
                    },
                },
            };

            var normalized = AppConfigStore.Normalize(root);
            var agent = normalized.Agents[AgentIds.InjectorAhk];

            Assert.Equal("Agents Driver", normalized.AutomationTools.Agent.PgDriver);
            Assert.Equal("require", normalized.AutomationTools.Agent.PgSsl);
            Assert.Equal(@"C:\Custom\agent.exe", agent.ExecutablePath);
            Assert.Equal(@"C:\Custom\agent.exe", normalized.AutomationTools.Ahk.ExecutablePath);
        }

        [Fact]
        public void Prefers_agent_path()
        {
            var sharedSettings = new AgentToolOptions
            {
                PgDriver = "{PostgreSQL ODBC Driver}",
                PgSsl = "require",
            };
            var root = new AppConfigRoot
            {
                AutomationTools =
                {
                    Ahk = new AhkToolOptions
                    {
                        ExecutablePath = @"C:\Legacy\pacinjector.exe",
                        ProcessName = "pacinjector",
                    },
                    Agent = sharedSettings,
                },
                Agents =
                {
                    [AgentIds.InjectorAhk] = new AgentInstanceConfig
                    {
                        ExecutablePath = @"D:\Agents\pactoolkits-injector.exe",
                        ProcessName = "pactoolkits-injector",
                        Settings = AgentSettingsSync.ToSettings(sharedSettings),
                    },
                },
            };

            var normalized = AppConfigStore.Normalize(root);
            var agent = normalized.Agents[AgentIds.InjectorAhk];

            Assert.Equal("{PostgreSQL ODBC Driver}", normalized.AutomationTools.Agent.PgDriver);
            Assert.Equal(@"D:\Agents\pactoolkits-injector.exe", agent.ExecutablePath);
            Assert.Equal("pactoolkits-injector", agent.ProcessName);
            Assert.Equal(@"D:\Agents\pactoolkits-injector.exe", normalized.AutomationTools.Ahk.ExecutablePath);
            Assert.Equal("pactoolkits-injector", normalized.AutomationTools.Ahk.ProcessName);
        }

        [Fact]
        public void Invalid_settings_fallback()
        {
            var root = new AppConfigRoot
            {
                AutomationTools =
                {
                    Ahk = new AhkToolOptions
                    {
                        ExecutablePath = AgentPaths.InjectorAhkExecutable,
                        ProcessName = "pactoolkits-injector",
                    },
                    Agent = new AgentToolOptions
                    {
                        PgDriver = "{PostgreSQL ODBC Driver}",
                        PgSsl = "require",
                    },
                },
                Agents =
                {
                    [AgentIds.InjectorAhk] = new AgentInstanceConfig
                    {
                        ExecutablePath = AgentPaths.InjectorAhkExecutable,
                        ProcessName = "pactoolkits-injector",
                        Settings = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["PgDriver"] = 123,
                        },
                    },
                },
            };

            var normalized = AppConfigStore.Normalize(root);
            var agent = normalized.Agents[AgentIds.InjectorAhk];

            Assert.Equal("{PostgreSQL ODBC Driver}", normalized.AutomationTools.Agent.PgDriver);
            Assert.Equal("require", normalized.AutomationTools.Agent.PgSsl);
            Assert.Equal("{PostgreSQL ODBC Driver}", AgentSettingsSync.FromSettings(agent.Settings!).PgDriver);
        }

        [Fact]
        public void Replaces_null_agent()
        {
            var root = new AppConfigRoot
            {
                AutomationTools =
                {
                    Ahk = new AhkToolOptions
                    {
                        ExecutablePath = AgentPaths.InjectorAhkExecutable,
                        ProcessName = "pactoolkits-injector",
                    },
                },
                Agents =
                {
                    [AgentIds.InjectorAhk] = null!,
                },
            };

            var normalized = AppConfigStore.Normalize(root);
            var agent = normalized.Agents[AgentIds.InjectorAhk];

            Assert.NotNull(agent);
            Assert.NotNull(agent.Runtime);
            Assert.NotNull(agent.Settings);
        }
    }
}
