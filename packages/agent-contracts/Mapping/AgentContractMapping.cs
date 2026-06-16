using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Agent.Contracts.Mapping;

public static class AgentContractMapping
{
    public static AgentRuntimeConfig ToRuntimeConfig(
        AhkToolOptions ahk,
        string unifiedConfigPath,
        string agentVersion)
        => new()
        {
            ExecutablePath = ahk.ExecutablePath,
            ProcessName = ahk.ProcessName,
            UnifiedConfigPath = unifiedConfigPath,
            AgentVersion = agentVersion,
        };

    public static AgentUiTargets ToUiTargets(AgentToolOptions agent)
        => new()
        {
            OptWindowClass = agent.OptWindowClass,
            IptWindowClass = agent.IptWindowClass,
            OptParseGrid = new ClassNnTarget(agent.OptParseGridClassNN),
            OptVerifyGrid = new ClassNnTarget(agent.OptVerifyGridClassNN),
            IptParseGrid = new ClassNnTarget(agent.IptParseGridClassNN),
            IptVerifyGrid = new ClassNnTarget(agent.IptVerifyGridClassNN),
            OptInput = new ClassNnTarget(agent.OptInputClassNN),
            IptInput = new ClassNnTarget(agent.IptInputClassNN),
        };

    public static IReadOnlyList<AgentWindowTarget> ToWindowTargets(AgentToolOptions agent)
    {
        if (agent.AppWin is null || agent.AppWin.Count == 0)
        {
            return Array.Empty<AgentWindowTarget>();
        }

        return agent.AppWin
            .Select(kv => new AgentWindowTarget(kv.Key, kv.Value, agent.OptWindowClass))
            .ToList();
    }

    public static WarehouseTaskIdentifier ToWarehouseTaskIdentifier(AgentToolOptions agent)
        => new(agent.WarehouseTaskIdentifier);
}
