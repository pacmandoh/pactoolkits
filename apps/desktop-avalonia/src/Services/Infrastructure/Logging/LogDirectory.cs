using System;
using PacToolkits.Agents.Contracts.Agents;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Logging;

/// <summary>
/// Logging.LogDirectory 存的是日志根目录（空 = 默认 …/logs）；Desktop 落盘在根目录下 desktop/
/// </summary>
public sealed record LogDirectoryResolution(
    string StoredDirectory,
    string RuntimeDirectory,
    string BrowseDirectory);

public static class LogDirectory
{
    public static LogDirectoryResolution Resolve(string? configuredDirectory)
    {
        var stored = NormalizeStoredPath(configuredDirectory);
        if (string.IsNullOrEmpty(stored))
        {
            return DefaultResolution();
        }

        var root = AgentsLogPaths.ResolveRoot(stored);
        if (IsDefaultRoot(root))
        {
            return DefaultResolution();
        }

        return new LogDirectoryResolution(
            root,
            AgentsLogPaths.DesktopDir(root),
            root);
    }

    private static LogDirectoryResolution DefaultResolution()
        => new(
            string.Empty,
            AgentsLogPaths.DesktopDir(null),
            AgentsLogPaths.DefaultLogsRoot());

    private static bool IsDefaultRoot(string root)
        => string.Equals(
            root,
            AgentsLogPaths.DefaultLogsRoot(),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string NormalizeStoredPath(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
