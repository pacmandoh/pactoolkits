namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Agents 与 Desktop 共用的 JSON 日志目录布局（根目录：…/PacToolkits/logs 或自定义）
/// </summary>
public static class AgentsLogPaths
{
    public const string ProductDirectoryName = "PacToolkits";
    public const string LogsDirectoryName = "logs";
    public const string DesktopDirectoryName = "desktop";
    public const string AgentsDirectoryName = "agents";
    public const string HostDirectoryName = "host";
    public const string ModulesDirectoryName = "modules";

    /// <summary>默认日志根：LocalAppData/PacToolkits/logs（不漫游）</summary>
    public static string DefaultLogsRoot()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductDirectoryName,
            LogsDirectoryName);

    /// <summary>空则用默认根目录；否则规范化为绝对路径</summary>
    public static string ResolveRoot(string? configuredRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return DefaultLogsRoot();
        }

        try
        {
            return Path.GetFullPath(
                configuredRoot.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return configuredRoot.Trim();
        }
    }

    public static string DesktopDir(string? configuredRoot = null)
        => Path.Combine(ResolveRoot(configuredRoot), DesktopDirectoryName);

    public static string AgentsRoot(string? configuredRoot = null)
        => Path.Combine(ResolveRoot(configuredRoot), AgentsDirectoryName);

    public static string HostDir(string? configuredRoot = null)
        => Path.Combine(AgentsRoot(configuredRoot), HostDirectoryName);

    public static string ModuleDir(string moduleId, string? configuredRoot = null)
    {
        var id = string.IsNullOrWhiteSpace(moduleId) ? "unknown" : moduleId.Trim();
        return Path.Combine(AgentsRoot(configuredRoot), ModulesDirectoryName, id);
    }
}
