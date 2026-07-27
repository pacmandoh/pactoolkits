namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Agents 布局与进程名常量（Host 入口、Modules 目录、用户 settings 路径）
/// </summary>
public static class AgentsPaths
{
    // Host = Agents 入口进程（Agents.exe）；Agents 是容器，业务进程按模块挂载
    public const string HostExecutableFileName = "Agents.exe";

    public const string HostExecutable = @".\Agents\Agents.exe";

    public const string HostProcessName = "Agents";

    // 模块启动自检通过后写入，供 Desktop / Host 判定模块就绪
    public const string ModuleReadyFileName = "module.ready";

    public const string ModuleManifestFileName = "module.json";

    // Desktop ↔ Host 控制文件（Modules/<Id>/）；每次写入一条命令：start / stop（quit 见 host.control）
    public const string ModuleControlFileName = "module.control";

    // Desktop ↔ Host 全局控制（Agents/ 根）；命令：quit
    public const string HostControlFileName = "host.control";

    public const string ModulesDirectoryName = "Modules";

    // Desktop 用户配置：{ConfigDir}/agents/modules/<Id>/settings.json（与安装目录分离）
    public const string UserAgentsDirectoryName = "agents";

    public const string UserModulesDirectoryName = "modules";

    public const string ModuleSettingsFileName = "settings.json";

    public const string ModuleSettingsSchemaFileName = "settings.schema.json";

    // Host → Module：模块业务配置路径参数
    public const string ModuleSettingsArgName = "--module-settings";

    public static string ModulesDir(string agentsDir)
        => Path.Combine(agentsDir, ModulesDirectoryName);

    public static string ModuleDir(string agentsDir, string moduleId)
        => Path.Combine(ModulesDir(agentsDir), moduleId);

    public static string ModuleControlPath(string agentsDir, string moduleId)
        => Path.Combine(ModuleDir(agentsDir, moduleId), ModuleControlFileName);

    public static string HostControlPath(string agentsDir)
        => Path.Combine(agentsDir, HostControlFileName);

    public static string ModuleReadyPath(string agentsDir, string moduleId)
        => Path.Combine(ModuleDir(agentsDir, moduleId), ModuleReadyFileName);

    public static string ModuleManifestPath(string agentsDir, string moduleId)
        => Path.Combine(ModuleDir(agentsDir, moduleId), ModuleManifestFileName);

    public static string ModuleDefaultSettingsPath(string agentsDir, string moduleId)
        => Path.Combine(ModuleDir(agentsDir, moduleId), ModuleSettingsFileName);

    public static string ModuleSettingsSchemaPath(string agentsDir, string moduleId)
        => Path.Combine(ModuleDir(agentsDir, moduleId), ModuleSettingsSchemaFileName);

    public static string UserAgentsDir(string configDir)
        => Path.Combine(configDir, UserAgentsDirectoryName);

    public static string UserModulesDir(string configDir)
        => Path.Combine(UserAgentsDir(configDir), UserModulesDirectoryName);

    public static string ModuleSettingsPath(string configDir, string moduleId)
        => Path.Combine(UserModulesDir(configDir), moduleId, ModuleSettingsFileName);
}
