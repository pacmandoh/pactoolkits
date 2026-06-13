namespace PacToolkits.Agent.Contracts.Models;

/// <summary>
/// Runtime launch parameters shared between desktop UI and the AHK agent process.
/// </summary>
public sealed class AgentRuntimeConfig
{
    public string ExecutablePath { get; set; } = @".\Tools\pacinjector.exe";

    public string ProcessName { get; set; } = string.Empty;

    public string UnifiedConfigPath { get; set; } = string.Empty;

    public string AgentVersion { get; set; } = string.Empty;
}
