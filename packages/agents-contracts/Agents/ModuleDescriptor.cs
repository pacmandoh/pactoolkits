namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Desktop 双态 Lucide 名（active=运行/启动中；inactive=未运行；供 <c>AppIcon.Kind</c>）
/// </summary>
public sealed record ModuleDesktopIcons(string Active, string Inactive);

/// <summary>
/// Desktop 壳层展示（底栏 / 顶栏状态胶囊）
/// </summary>
public sealed record ModuleDesktop(
    ModuleDesktopIcons Icons,
    bool BottomStatusBar,
    bool TopStatusPills,
    int Order);

/// <summary>
/// Ahk2Exe 工具参数（相对模块目录）
/// </summary>
public sealed record ModuleAhk2Exe(string Icon);

/// <summary>
/// win-x64 打包配方：平台固定为 win-x64；<see cref="Builder"/> 选工具，同名对象承载参数
/// </summary>
public sealed record ModulePackage(
    string Builder,
    ModuleAhk2Exe Ahk2Exe);

/// <summary>
/// 已解析的 <c>module.json</c>（含模块目录，供 Host / Desktop / CI 发现）
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
    ModulePackage Package);

/// <summary>
/// <c>module.json</c> 的 <c>runtime</c> 取值（进程怎么跑）
/// </summary>
public static class ModuleRuntimes
{
    public const string Ahk = "ahk";
}

/// <summary>
/// <c>package.builder</c> 取值（CI 用什么工具打 win-x64 制品）
/// </summary>
public static class ModuleBuilders
{
    public const string Ahk2Exe = "ahk2exe";
}

/// <summary>
/// 唯一支持的模块制品 RID（与 Agents CI <c>RUNTIME=win-x64</c> 对齐）
/// </summary>
public static class ModuleRids
{
    public const string WinX64 = "win-x64";
}
