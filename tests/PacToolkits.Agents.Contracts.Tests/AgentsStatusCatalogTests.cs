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
            MinDbSchema: "1.2.20",
            MaxDbSchema: "1.2.25");

        AgentsStatusCatalog.FillCatalog(status.Modules[0], descriptor);
        var list = AgentsStatusCatalog.ToDescriptors(status, agentsDir: "/Agents");
        Assert.Single(list);
        var back = list[0];
        Assert.Equal("Injector", back.Id);
        Assert.Equal("2.0.0", back.Version);
        Assert.Equal("注入", back.DisplayName);
        Assert.Equal("a.png", back.Desktop.Icons.Active);
        Assert.Equal(3, back.Desktop.Order);
        Assert.True(back.RequiresDatabase);
        Assert.Equal("1.2.20", back.MinDbSchema);
        Assert.Equal("1.2.25", back.MaxDbSchema);
    }
}
