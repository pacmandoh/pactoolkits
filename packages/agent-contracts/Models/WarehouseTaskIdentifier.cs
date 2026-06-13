namespace PacToolkits.Agent.Contracts.Models;

/// <summary>
/// Column spec used by the warehouse flow to locate a task row (e.g. "单据号||当前编号").
/// </summary>
public sealed record WarehouseTaskIdentifier(string Spec)
{
    public override string ToString() => Spec;
}
