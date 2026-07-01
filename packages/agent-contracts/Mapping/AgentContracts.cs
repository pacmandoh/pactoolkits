using PacToolkits.Agent.Contracts.Models;

namespace PacToolkits.Agent.Contracts.Mapping;

public static class AgentContracts
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
}
