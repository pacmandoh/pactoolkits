namespace PacToolkits.Agents.Contracts.Agents;

public static class AgentsIds
{
    public const string Agents = "agents";
}

public sealed record AgentsDescriptor(string Id);

public static class AgentsDescriptors
{
    public static AgentsDescriptor Agents { get; } = new(AgentsIds.Agents);
}
