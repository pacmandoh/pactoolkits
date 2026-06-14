namespace PacToolkits.Agent.Contracts.Agents;

public readonly record struct AgentId(string Value)
{
    public static AgentId InjectorAhk { get; } = new(AgentIds.InjectorAhk);

    public override string ToString() => Value;

    public static implicit operator string(AgentId id) => id.Value;

    public static implicit operator AgentId(string value) => new(value);
}
