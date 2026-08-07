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

    // 模块仅在自检完成后创建该文件；Host 读入并写入 host.status.json
    public const string ModuleReadyFileName = "module.ready";

    public const string ModuleManifestFileName = "module.json";

    // Host 周期发布的运行快照（IPC 推送 + 诊断落盘）
    public const string HostStatusFileName = "host.status.json";

    // 期望挂载集合（IPC desired 热路径；文件为诊断/冷启动镜像）
    public const string HostDesiredFileName = "host.desired.json";

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

    public static string HostStatusPath(string agentsDir)
        => Path.Combine(agentsDir, HostStatusFileName);

    public static string HostDesiredPath(string agentsDir)
        => Path.Combine(agentsDir, HostDesiredFileName);

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
