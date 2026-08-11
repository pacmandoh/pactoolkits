namespace PacToolkits.Application.DTOs;

/// <summary>别名编辑可用的机器名来源</summary>
public sealed record ClientAliasSources(
    bool IsDbConnected,
    IReadOnlyList<string> ClientMachines);
