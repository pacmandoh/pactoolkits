namespace PacToolkits.Agent.Contracts.Models;

/// <summary>
/// Maps a host process executable to a UI automation window class.
/// </summary>
public sealed record AgentWindowTarget(string ProcessExecutable, int MatchMode, string WindowClass);
