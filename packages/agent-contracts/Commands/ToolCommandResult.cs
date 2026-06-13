namespace PacToolkits.Agent.Contracts.Commands;

public enum ToolRunState
{
    Stopped,
    Running,
    Unknown
}

public readonly record struct ToolCommandResult(bool Ok, string Message, bool SuppressToast = false);
