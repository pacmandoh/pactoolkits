namespace PacToolkits.Agent.Contracts.Models;

public sealed class AgentInstanceConfig
{
    public bool Enabled { get; set; } = true;

    public string ExecutablePath { get; set; } = string.Empty;

    public string ProcessName { get; set; } = string.Empty;

    public Dictionary<string, object?> Runtime { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, object?> Settings { get; set; } = new(StringComparer.Ordinal);
}
