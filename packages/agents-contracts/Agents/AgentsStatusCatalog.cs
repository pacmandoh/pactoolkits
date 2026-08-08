namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// StatusSnapshot 与 ModuleDescriptor 映射（UI 目录）
/// </summary>
public static class AgentsStatusCatalog
{
    public static IReadOnlyList<ModuleDescriptor> ToDescriptors(
        AgentsStatus status,
        string agentsDir)
    {
        var list = new List<ModuleDescriptor>(status.Modules.Count);
        foreach (var m in status.Modules)
        {
            if (string.IsNullOrWhiteSpace(m.Id))
            {
                continue;
            }

            var dir = AgentsPaths.ModuleDir(agentsDir, m.Id);
            list.Add(new ModuleDescriptor(
                Id: m.Id,
                Version: m.Version ?? "未知",
                Runtime: m.Runtime ?? ModuleRuntimes.Ahk,
                DisplayName: string.IsNullOrWhiteSpace(m.DisplayName) ? m.Id : m.DisplayName!,
                EntryWinX64: m.EntryWinX64 ?? string.Empty,
                Directory: dir,
                ManifestPath: AgentsPaths.ModuleManifestPath(agentsDir, m.Id),
                Desktop: new ModuleDesktop(
                    new ModuleDesktopIcons(
                        m.IconActive ?? string.Empty,
                        m.IconInactive ?? string.Empty),
                    m.BottomStatusBar,
                    m.TopStatusPills,
                    m.Order),
                Package: new ModulePackage(ModuleBuilders.Ahk2Exe, new ModuleAhk2Exe(string.Empty)),
                MinDbSchema: m.MinDbSchema,
                MaxDbSchema: m.MaxDbSchema));
        }

        return list
            .OrderBy(static d => d.Desktop.Order)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static void FillCatalog(AgentsStatusModule wire, ModuleDescriptor descriptor)
    {
        wire.Version = descriptor.Version;
        wire.DisplayName = descriptor.DisplayName;
        wire.Runtime = descriptor.Runtime;
        wire.EntryWinX64 = descriptor.EntryWinX64;
        wire.MinDbSchema = descriptor.MinDbSchema;
        wire.MaxDbSchema = descriptor.MaxDbSchema;
        wire.IconActive = descriptor.Desktop.Icons.Active;
        wire.IconInactive = descriptor.Desktop.Icons.Inactive;
        wire.BottomStatusBar = descriptor.Desktop.BottomStatusBar;
        wire.TopStatusPills = descriptor.Desktop.TopStatusPills;
        wire.Order = descriptor.Desktop.Order;
    }
}
