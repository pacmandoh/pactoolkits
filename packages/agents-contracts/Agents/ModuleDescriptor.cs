namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// 模块运行态与非运行态对应的 Desktop 图标名称
/// </summary>
public sealed record ModuleDesktopIcons(string Active, string Inactive);

/// <summary>
/// 模块在 Desktop 顶栏和状态栏中的展示策略
/// </summary>
public sealed record ModuleDesktop(
    ModuleDesktopIcons Icons,
    bool BottomStatusBar,
    bool TopStatusPills,
    int Order);

/// <summary>
/// Ahk2Exe 构建参数；路径均相对模块目录
/// </summary>
public sealed record ModuleAhk2Exe(string Icon);

/// <summary>
/// 模块的 win-x64 构建配方
/// </summary>
public sealed record ModulePackage(
    string Builder,
    ModuleAhk2Exe Ahk2Exe);

/// <summary>
/// 已验证的模块描述及其源码或安装目录
///
/// 顶层完整 minApiContract+maxApiContract 即依赖 PacAPI 协议门禁；两者皆缺=不校验协议；半套拒绝
/// </summary>
public sealed record ModuleDescriptor(
    string Id,
    string Version,
    string Runtime,
    string DisplayName,
    string EntryWinX64,
    string Directory,
    string ManifestPath,
    ModuleDesktop Desktop,
    ModulePackage Package,
    string? MinApiContract = null,
    string? MaxApiContract = null,
    IReadOnlyList<string>? RequiredApiScopes = null)
{
    public bool RequiresApiContract
        => !string.IsNullOrWhiteSpace(MinApiContract) && !string.IsNullOrWhiteSpace(MaxApiContract);

    public bool RequiresApi
        => RequiredApiScopes is { Count: > 0 } || RequiresApiContract;
}

/// <summary>
/// <c>module.json</c> 支持的运行时标识
/// </summary>
public static class ModuleRuntimes
{
    public const string Ahk = "ahk";
}

/// <summary>
/// <c>package.builder</c> 支持的构建器标识
/// </summary>
public static class ModuleBuilders
{
    public const string Ahk2Exe = "ahk2exe";
}

/// <summary>
/// Agents 模块发布支持的 RID
/// </summary>
public static class ModuleRids
{
    public const string WinX64 = "win-x64";
}
