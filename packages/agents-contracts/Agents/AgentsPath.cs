using System.Text.Json;

namespace PacToolkits.Agents.Contracts.Agents;

/// <summary>
/// Host 可执行路径的解析来源
/// </summary>
public enum HostExecutableResolutionSource
{
    Configured,
    Standard,
    Missing,
}

/// <summary>
/// Host 路径解析结果，同时提供运行路径和规范化配置值
/// </summary>
public sealed record HostExecutableResolution(
    string? ResolvedPath,
    string StoredPath,
    HostExecutableResolutionSource Source);

/// <summary>
/// 解析 Agents 安装布局和模块描述文件，不执行文件写入或进程控制
/// </summary>
public static class AgentsPath
{
    private const string EntryWinX64Property = ModuleRids.WinX64;

    public static string? TryResolveModuleEntryPath(string agentsDir, string moduleId)
    {
        if (string.IsNullOrWhiteSpace(agentsDir) || !IsValidModuleId(moduleId))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(AgentsPaths.ModuleManifestPath(agentsDir, moduleId));
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            var manifestId = ReadRequiredString(root, "id");
            if (!string.Equals(manifestId, moduleId, StringComparison.Ordinal)
                || !root.TryGetProperty("entry", out var entry)
                || entry.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var fileName = ReadRequiredString(entry, EntryWinX64Property);
            return IsSafeFileName(fileName)
                ? ResolveRelativePath(fileName!, AgentsPaths.ModuleDir(agentsDir, moduleId))
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static string? TryReadModuleVersion(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            return ReadRequiredString(doc.RootElement, "version");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 读取完整模块描述；缺少运行、桌面或构建元数据时返回 <see langword="null"/>
    /// </summary>
    public static ModuleDescriptor? TryReadModule(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var doc = JsonDocument.Parse(stream);
            return TryParseModule(doc.RootElement, Path.GetFullPath(manifestPath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 扫描模块目录，忽略无效描述和 ID 不匹配项，并按桌面顺序及 ID 返回稳定结果
    /// </summary>
    public static IReadOnlyList<ModuleDescriptor> ScanModules(string agentsDir)
    {
        if (string.IsNullOrWhiteSpace(agentsDir))
        {
            return [];
        }

        string modulesRoot;
        try
        {
            modulesRoot = Path.GetFullPath(AgentsPaths.ModulesDir(agentsDir));
        }
        catch
        {
            return [];
        }

        if (!Directory.Exists(modulesRoot))
        {
            return [];
        }

        var results = new List<ModuleDescriptor>();
        string[] moduleDirectories;
        try
        {
            moduleDirectories = Directory.GetDirectories(modulesRoot);
        }
        catch
        {
            return [];
        }

        foreach (var moduleDir in moduleDirectories)
        {
            var folderName = Path.GetFileName(moduleDir);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            var manifestPath = Path.Combine(moduleDir, AgentsPaths.ModuleManifestFileName);
            var module = TryReadModule(manifestPath);
            if (module is null)
            {
                continue;
            }

            // 目录名为模块 ID，描述文件 id 必须与之完全匹配
            if (!string.Equals(module.Id, folderName, StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(module);
        }

        results.Sort(static (a, b) =>
        {
            var byOrder = a.Desktop.Order.CompareTo(b.Desktop.Order);
            return byOrder != 0
                ? byOrder
                : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });
        return results;
    }

    /// <summary>比较影响发现、启动和桌面展示的模块元数据</summary>
    public static bool CatalogEquals(
        IReadOnlyList<ModuleDescriptor>? left,
        IReadOnlyList<ModuleDescriptor>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 解析模块目录内的 Ahk2Exe 图标路径；绝对路径或目录外路径返回 <see langword="null"/>
    /// </summary>
    public static string? TryResolveAhk2ExeIconPath(ModuleDescriptor module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (string.IsNullOrWhiteSpace(module.Package.Ahk2Exe.Icon))
        {
            return null;
        }

        return ResolveRelativePath(module.Package.Ahk2Exe.Icon, module.Directory);
    }

    public static string? TryReadHostVersion(string releaseManifestPath)
    {
        if (string.IsNullOrWhiteSpace(releaseManifestPath) || !File.Exists(releaseManifestPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(releaseManifestPath);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("components", out var components)
                || components.ValueKind != JsonValueKind.Object
                || !components.TryGetProperty("agents", out var agents)
                || agents.ValueKind != JsonValueKind.Object
                || !agents.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = version.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static HostExecutableResolution ResolveHost(string? configuredPath, string baseDirectory)
    {
        var configured = NormalizeStoredPath(configuredPath);
        var standardStored = AgentsPaths.HostExecutable;
        var resolvedStandard = ResolvePath(standardStored, baseDirectory);
        var standardExists = resolvedStandard is not null && File.Exists(resolvedStandard);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var resolvedConfigured = ResolvePath(configured, baseDirectory);
            if (resolvedConfigured is not null && File.Exists(resolvedConfigured))
            {
                return new HostExecutableResolution(
                    resolvedConfigured,
                    configured,
                    HostExecutableResolutionSource.Configured);
            }
        }

        if (standardExists)
        {
            return new HostExecutableResolution(
                resolvedStandard,
                standardStored,
                HostExecutableResolutionSource.Standard);
        }

        return new HostExecutableResolution(
            null,
            string.IsNullOrWhiteSpace(configured) ? standardStored : configured,
            HostExecutableResolutionSource.Missing);
    }

    public static string? ResolvePath(string? value, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var trimmed = value.Trim().Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(trimmed))
            {
                return Path.GetFullPath(trimmed);
            }

            return Path.GetFullPath(trimmed, baseDirectory);
        }
        catch
        {
            return null;
        }
    }

    public static bool IsValidModuleId(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId))
        {
            return false;
        }

        var id = moduleId.Trim();
        return id.Length == moduleId.Length
               && char.IsAsciiLetterOrDigit(id[0])
               && id.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    }

    private static ModuleDescriptor? TryParseModule(JsonElement root, string manifestPath)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var id = ReadRequiredString(root, "id");
        var version = ReadRequiredString(root, "version");
        if (id is null || !IsValidModuleId(id) || version is null)
        {
            return null;
        }

        if (!root.TryGetProperty("entry", out var entry) || entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var entryWinX64 = ReadRequiredString(entry, EntryWinX64Property);
        if (entryWinX64 is null || !IsSafeFileName(entryWinX64))
        {
            return null;
        }

        var runtime = ReadRequiredString(root, "runtime");
        var displayName = ReadRequiredString(root, "displayName");
        if (runtime is null || displayName is null)
        {
            return null;
        }

        var desktop = TryParseDesktop(root);
        if (desktop is null)
        {
            return null;
        }

        var package = TryParsePackage(root, runtime);
        if (package is null)
        {
            return null;
        }

        // 顶层契约：模块运行是否依赖库；与 desktop 展示无关；缺省 true
        var requiresDatabase = ReadBool(root, "requiresDatabase") ?? true;

        string directory;
        try
        {
            directory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        return new ModuleDescriptor(
            id,
            version,
            runtime,
            displayName,
            entryWinX64,
            directory,
            manifestPath,
            desktop,
            package,
            requiresDatabase);
    }

    private static ModuleDesktop? TryParseDesktop(JsonElement root)
    {
        if (!root.TryGetProperty("desktop", out var desktop) || desktop.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!desktop.TryGetProperty("icons", out var icons) || icons.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var active = ReadRequiredString(icons, "active");
        var inactive = ReadRequiredString(icons, "inactive");
        var bottomStatusBar = ReadBool(desktop, "bottomStatusBar");
        var topStatusPills = ReadBool(desktop, "topStatusPills");
        var order = ReadRequiredInt(desktop, "order");
        if (active is null
            || inactive is null
            || bottomStatusBar is null
            || topStatusPills is null
            || order is null)
        {
            return null;
        }

        return new ModuleDesktop(
            new ModuleDesktopIcons(active, inactive),
            bottomStatusBar.Value,
            topStatusPills.Value,
            order.Value);
    }

    private static ModulePackage? TryParsePackage(JsonElement root, string runtime)
    {
        if (!root.TryGetProperty("package", out var package) || package.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var builder = ReadRequiredString(package, "builder");
        if (builder is null)
        {
            return null;
        }

        // 当前发布协议仅接受 win-x64 AHK 模块，未知运行时或构建器必须在发现阶段拒绝
        if (!string.Equals(runtime, ModuleRuntimes.Ahk, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(builder, ModuleBuilders.Ahk2Exe, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!package.TryGetProperty(ModuleBuilders.Ahk2Exe, out var ahk2exe)
            || ahk2exe.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var icon = ReadRequiredString(ahk2exe, "icon");
        if (icon is null || ResolveRelativePath(icon, AppContext.BaseDirectory) is null)
        {
            return null;
        }

        return new ModulePackage(ModuleBuilders.Ahk2Exe, new ModuleAhk2Exe(icon));
    }

    private static string? ReadRequiredString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool? ReadBool(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static int? ReadRequiredInt(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool IsSafeFileName(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && value is not "." and not ".."
           && value.IndexOfAny(['/', '\\', '<', '>', ':', '"', '|', '?', '*']) < 0
           && string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal)
           && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static string? ResolveRelativePath(string value, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
        {
            return null;
        }

        try
        {
            var root = Path.GetFullPath(baseDirectory);
            var resolved = Path.GetFullPath(value.Replace('\\', Path.DirectorySeparatorChar), root);
            var relative = Path.GetRelativePath(root, resolved);
            return relative is "." or ".."
                   || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? null
                : resolved;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeStoredPath(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
