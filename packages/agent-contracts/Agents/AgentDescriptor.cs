namespace PacToolkits.Agent.Contracts.Agents;

public sealed record AgentDescriptor(
    string Id,
    string DisplayName,
    string Implementation,
    string ExecutableFileName);
