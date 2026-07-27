namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// 定义 Desktop、Host 和模块共同使用的 Agents 文件布局与命令行参数
/// </summary>
public static class AgentsPaths
{
    // Host 文件名是发布协议的一部分，Desktop 默认路径和构建产物必须保持一致
    public const string HostExecutableFileName = "Agents.exe";

    public const string HostExecutable = @".\Agents\Agents.exe";

    public const string HostProcessName = "Agents";

    // 模块仅在自检完成后创建该文件，Desktop 将其与进程状态共同作为就绪条件
    public const string ModuleReadyFileName = "module.ready";

    public const string ModuleManifestFileName = "module.json";

    // 模块控制文件一次承载一条 start 或 stop 命令，Host 读取后删除
    public const string ModuleControlFileName = "module.control";

    // Host 控制文件当前仅支持 quit，独立于模块控制文件
    public const string HostControlFileName = "host.control";

    public const string ModulesDirectoryName = "Modules";

    // 用户配置位于可写配置目录，避免模块升级覆盖业务配置
    public const string UserAgentsDirectoryName = "agents";

    public const string UserModulesDirectoryName = "modules";

    public const string ModuleSettingsFileName = "settings.json";

    public const string ModuleSettingsSchemaFileName = "settings.schema.json";

    // Host 通过该参数向模块传递独立业务配置路径
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
