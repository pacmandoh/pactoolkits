namespace PacToolkits.Agent.Contracts.Agents;

public static class AgentDescriptors
{
    public static AgentDescriptor InjectorAhk { get; } = new(
        Id: AgentIds.InjectorAhk,
        DisplayName: "PacToolkits Agent Injector AHK",
        Implementation: "ahk",
        ExecutableFileName: AgentPaths.InjectorAhkExecutableFileName);

    public static IReadOnlyList<AgentDescriptor> All { get; } = [InjectorAhk];
}
