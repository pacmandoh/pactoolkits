using System;
using System.IO;

namespace PacToolkits.Desktop.Avalonia.Common;

public sealed record LogDirectoryResolution(
    string StoredDirectory,
    string RuntimeDirectory,
    bool RequiresMigration);

public static class LogDirectory
{
    public const string LogsSegment = "logs";
    public const string LegacySubdirectory = "ui";
    public const string CurrentSubdirectory = "desktop";

    public static LogDirectoryResolution Resolve(string? configuredDirectory)
    {
        var stored = NormalizeStoredPath(configuredDirectory);
        if (string.IsNullOrWhiteSpace(stored))
        {
            var runtime = GetDefaultDirectory();
            return new LogDirectoryResolution(
                string.Empty,
                runtime,
                RequiresMigration: false);
        }

        if (IsLegacyLogsDirectory(stored))
        {
            var migrated = MigrateLegacyLogsDirectory(stored);
            return new LogDirectoryResolution(
                migrated,
                migrated,
                RequiresMigration: true);
        }

        var configuredRuntime = ResolveRuntimePath(stored);
        return new LogDirectoryResolution(
            stored,
            configuredRuntime,
            RequiresMigration: false);
    }

    public static string GetDefaultDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(baseDir, "PacToolkits", LogsSegment, CurrentSubdirectory);
    }

    public static bool IsLegacyLogsDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = NormalizeFullPath(path);
            return EndsWithLogsSubdirectory(fullPath, LegacySubdirectory);
        }
        catch
        {
            return false;
        }
    }

    private static string MigrateLegacyLogsDirectory(string legacyPath)
    {
        var fullPath = NormalizeFullPath(legacyPath);
        if (!EndsWithLogsSubdirectory(fullPath, LegacySubdirectory))
        {
            return legacyPath.Trim();
        }

        var logsDirectory = Path.GetDirectoryName(fullPath)
                              ?? throw new InvalidOperationException("Invalid legacy log directory.");
        var rootDirectory = Path.GetDirectoryName(logsDirectory)
                            ?? throw new InvalidOperationException("Invalid legacy log directory.");
        return Path.Combine(rootDirectory, LogsSegment, CurrentSubdirectory);
    }

    private static string ResolveRuntimePath(string storedDirectory)
    {
        try
        {
            return NormalizeFullPath(storedDirectory);
        }
        catch
        {
            return storedDirectory.Trim();
        }
    }

    private static string NormalizeFullPath(string path)
        => Path.GetFullPath(path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    private static bool EndsWithLogsSubdirectory(string fullPath, string subdirectory)
    {
        var leaf = Path.GetFileName(fullPath);
        if (!string.Equals(leaf, subdirectory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var logsDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(logsDirectory))
        {
            return false;
        }

        return string.Equals(
            Path.GetFileName(logsDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            LogsSegment,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeStoredPath(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
