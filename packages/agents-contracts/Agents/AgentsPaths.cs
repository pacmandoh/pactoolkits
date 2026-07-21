namespace PacToolkits.Agents.Contracts.Agents;

public static class AgentsPaths
{
    /// <summary>
    /// Host = Agents entry process that hosts modules (Agents.exe).
    /// Not a product name; Agents is the container, Injector is a module.
    /// </summary>
    public const string HostExecutableFileName = "Agents.exe";

    public const string HostExecutable = @".\Agents\Agents.exe";

    public const string HostProcessName = "Agents";

    public const string InjectorModuleId = "Injector";

    /// <summary>Written by a module after startup self-check passes.</summary>
    public const string ModuleReadyFileName = "module.ready";

    public const string ModuleManifestFileName = "module.json";

    /// <summary>
    /// Desktop ↔ Host control file under <c>Modules/&lt;Id&gt;/</c>.
    /// One command per write: <c>start</c>, <c>stop</c>, or <c>quit</c>.
    /// </summary>
    public const string ModuleControlFileName = "module.control";

    public const string ModulesDirectoryName = "Modules";

    /// <summary>Main-shipped layout (config migration source only).</summary>
    public const string MainToolsExecutable = @".\Tools\pacinjector.exe";

    public const string MainToolsFileName = "pacinjector.exe";

    public const string MainToolsProcessName = "pacinjector";

    /// <summary>Host entry process names for status/stop (includes Main Tools process).</summary>
    public static readonly string[] HostProcessNameCandidates =
    [
        HostProcessName,
        MainToolsProcessName,
    ];

    public static string ModuleDir(string agentsDir, string moduleId)
        => Path.Combine(agentsDir, ModulesDirectoryName, moduleId);

    public static string ModuleControlPath(string agentsDir, string moduleId)
        => Path.Combine(ModuleDir(agentsDir, moduleId), ModuleControlFileName);

    public static string ModuleReadyPath(string agentsDir, string moduleId)
        => Path.Combine(ModuleDir(agentsDir, moduleId), ModuleReadyFileName);

    public static string ModuleManifestPath(string agentsDir, string moduleId)
        => Path.Combine(ModuleDir(agentsDir, moduleId), ModuleManifestFileName);
}
