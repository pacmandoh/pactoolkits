namespace PacToolkits.Agents.Contracts.Commands;

public readonly record struct AgentsCommandResult(bool Ok, string Message, bool SuppressToast = false);
