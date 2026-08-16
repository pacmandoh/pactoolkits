using PacToolkits.Agents.Contracts.Agents;
using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Tests;

public sealed class MainWindowChromeTests
{
    [Fact]
    public void Chrome_key_changes_when_module_display_slot_changes()
    {
        var dual = Descriptor(top: true, bottom: true);
        var topOnly = dual with
        {
            Desktop = dual.Desktop with { BottomStatusBar = false },
        };

        var dualKey = MainWindowViewModel.BuildAgentsChromeKey(
            AgentsRunState.Running,
            [dual],
            _ => AgentsRunState.Running);
        var topOnlyKey = MainWindowViewModel.BuildAgentsChromeKey(
            AgentsRunState.Running,
            [topOnly],
            _ => AgentsRunState.Running);
        var dualAgain = MainWindowViewModel.BuildAgentsChromeKey(
            AgentsRunState.Running,
            [dual],
            _ => AgentsRunState.Running);

        Assert.NotEqual(dualKey, topOnlyKey);
        Assert.Equal(dualKey, dualAgain);
    }

    private static ModuleDescriptor Descriptor(bool top, bool bottom)
        => new(
            Id: "Injector",
            Version: "1.0.0",
            Runtime: ModuleRuntimes.Ahk,
            DisplayName: "注入",
            EntryWinX64: "Injector.exe",
            Directory: "/Agents/modules/Injector",
            ManifestPath: "/Agents/modules/Injector/module.json",
            Desktop: new ModuleDesktop(
                new ModuleDesktopIcons("on.png", "off.png"),
                BottomStatusBar: bottom,
                TopStatusPills: top,
                Order: 1),
            Package: new ModulePackage(ModuleBuilders.Ahk2Exe, new ModuleAhk2Exe(string.Empty)));
}
