namespace PacToolkits.Agents.Contracts.Agents;

public static class AgentsIds
{
    public const string Agents = "agents";
}

/// <summary>
/// Agents 运行时描述（当前仅 Id；扩展字段放此处以免散落）
/// </summary>
public sealed record AgentsDescriptor(string Id);

public static class AgentsDescriptors
{
    public static AgentsDescriptor Agents { get; } = new(AgentsIds.Agents);
}
