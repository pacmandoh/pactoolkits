using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Agents.Host;

/// <summary>
/// host.desired.json 读写（启停与 reconcile 在 Program）
/// </summary>
internal static class HostDesired
{
    public static HashSet<string> ReadMountIds(string agentsDir)
        => AgentsDesired.TryReadIds(agentsDir) ?? new HashSet<string>(StringComparer.Ordinal);

    public static void Clear(string agentsDir)
        => AgentsDesired.TryDelete(agentsDir);
}
