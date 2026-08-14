using System.Text.Json;
using PacToolkits.Agents.Contracts.Agents;
using Xunit;

namespace PacToolkits.Agents.Contracts.Tests;

public sealed class AgentsStatusCatalogTests
{
    [Fact]
    public void FillCatalog_and_ToDescriptors_round_trip()
    {
        var status = AgentsStatus.Create(
            1,
            [
                new AgentsStatusModule
                {
                    Id = "Injector",
                    Pid = 10,
                    Ready = true,
                    State = AgentsRunState.Running,
                },
            ]);

        var descriptor = new ModuleDescriptor(
            Id: "Injector",
            Version: "2.0.0",
            Runtime: ModuleRuntimes.Ahk,
            DisplayName: "注入",
            EntryWinX64: "Injector.exe",
            Directory: "/tmp/Injector",
            ManifestPath: "/tmp/Injector/module.json",
            Desktop: new ModuleDesktop(
                new ModuleDesktopIcons("a.png", "b.png"),
                BottomStatusBar: true,
                TopStatusPills: true,
                Order: 3),
            Package: new ModulePackage(ModuleBuilders.Ahk2Exe, new ModuleAhk2Exe(string.Empty)),
            MinApiContract: "1.2.20",
            MaxApiContract: "1.2.25",
            RequiredApiScopes: ["read", "write"]);

        AgentsStatusCatalog.FillCatalog(status.Modules[0], descriptor);
        var list = AgentsStatusCatalog.ToDescriptors(status, agentsDir: "/Agents");
        Assert.Single(list);
        var back = list[0];
        Assert.Equal("Injector", back.Id);
        Assert.Equal("2.0.0", back.Version);
        Assert.Equal("注入", back.DisplayName);
        Assert.Equal("a.png", back.Desktop.Icons.Active);
        Assert.Equal(3, back.Desktop.Order);
        Assert.True(back.RequiresApiContract);
        Assert.Equal("1.2.20", back.MinApiContract);
        Assert.Equal("1.2.25", back.MaxApiContract);
        Assert.Equal(["read", "write"], back.RequiredApiScopes);
        Assert.True(back.RequiresApi);
    }

    [Fact]
    public void ToDescriptors_keeps_scopes_without_contract_range()
    {
        var status = AgentsStatus.Create(
            1,
            [
                new AgentsStatusModule { Id = "Injector" },
            ]);

        var descriptor = new ModuleDescriptor(
            Id: "Injector",
            Version: "2.0.0",
            Runtime: ModuleRuntimes.Ahk,
            DisplayName: "注入",
            EntryWinX64: "Injector.exe",
            Directory: "/tmp/Injector",
            ManifestPath: "/tmp/Injector/module.json",
            Desktop: new ModuleDesktop(
                new ModuleDesktopIcons("a.png", "b.png"),
                BottomStatusBar: false,
                TopStatusPills: true,
                Order: 1),
            Package: new ModulePackage(ModuleBuilders.Ahk2Exe, new ModuleAhk2Exe(string.Empty)),
            RequiredApiScopes: ["read"]);

        AgentsStatusCatalog.FillCatalog(status.Modules[0], descriptor);
        var json = JsonSerializer.Serialize(status, AgentsJson.Options);
        var loaded = JsonSerializer.Deserialize<AgentsStatus>(json, AgentsJson.Options);
        Assert.NotNull(loaded);
        var list = AgentsStatusCatalog.ToDescriptors(loaded, agentsDir: "/Agents");
        Assert.Single(list);
        Assert.False(list[0].RequiresApiContract);
        Assert.Equal(["read"], list[0].RequiredApiScopes);
        Assert.True(list[0].RequiresApi);
    }
}
