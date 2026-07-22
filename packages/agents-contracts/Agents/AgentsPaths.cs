namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Agents 布局与进程名常量（Host 入口、Modules 目录、Legacy Tools 迁移源）
/// </summary>
public static class AgentsPaths
{
    // Host = Agents 入口进程（Agents.exe）；Agents 是容器，Injector 是挂载模块
    public const string HostExecutableFileName = "Agents.exe";

    public const string HostExecutable = @".\Agents\Agents.exe";

    public const string HostProcessName = "Agents";

    public const string InjectorModuleId = "Injector";

    // 模块启动自检通过后写入，供 Desktop / Host 判定 Injector 就绪
    public const string ModuleReadyFileName = "module.ready";

    public const string ModuleManifestFileName = "module.json";

    // Desktop ↔ Host 控制文件（Modules/<Id>/）；每次写入一条命令：start / stop / quit
    public const string ModuleControlFileName = "module.control";

    public const string ModulesDirectoryName = "Modules";

    // Legacy Tools 已发布布局；仅作配置迁移源，不是运行时兜底扫描路径
    public const string LegacyToolsExecutable = @".\Tools\pacinjector.exe";

    public const string LegacyToolsFileName = "pacinjector.exe";

    public const string LegacyToolsProcessName = "pacinjector";

    // 状态探测 / 停止时包含 Legacy Tools 进程名，便于迁移后清掉旧 pacinjector
    public static readonly string[] HostProcessNameCandidates =
    [
        HostProcessName,
        LegacyToolsProcessName,
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
