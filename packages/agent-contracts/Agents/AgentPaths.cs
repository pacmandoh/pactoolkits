namespace PacToolkits.Agent.Contracts.Agents;

public static class AgentPaths
{
    public const string InjectorAhkId = AgentIds.InjectorAhk;

    public const string InjectorAhkExecutableFileName = "pactoolkits-injector.exe";

    public const string InjectorAhkRelativeDirectory = @"Agents\injector";

    public const string InjectorAhkExecutable = @".\Agents\injector\pactoolkits-injector.exe";

    public const string InjectorProcessName = "pactoolkits-injector";

    /// <summary>Pre-rename bundled layout (migration source).</summary>
    public const string PreviousInjectorAhkRelativeDirectory = @"Agents\agent-injector-ahk";

    public const string PreviousInjectorAhkExecutableFileName = "pactoolkits-agent-injector-ahk.exe";

    public const string PreviousInjectorAhkExecutable =
        @".\Agents\agent-injector-ahk\pactoolkits-agent-injector-ahk.exe";

    public const string LegacyInjectorExecutable = @".\Tools\pacinjector.exe";

    public const string LegacyInjectorFileName = "pacinjector.exe";

    public static readonly string[] InjectorAhkProcessNameCandidates =
    [
        InjectorProcessName,
        "pactoolkits-agent-injector-ahk",
        "pacinjector",
    ];
}
