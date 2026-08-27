namespace PacToolkits.Agents.Contracts.Agents;

public static class AgentsIds
{
    public const string Agents = "agents";
}

/// <summary>
/// 标识 Agents 运行时实例；Id 是后续运行时元数据的稳定键
/// </summary>
public sealed record AgentsDescriptor(string Id);

public static class AgentsDescriptors
{
    public static AgentsDescriptor Agents { get; } = new(AgentsIds.Agents);
}
