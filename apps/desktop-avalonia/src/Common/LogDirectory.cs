using System;
using System.IO;

namespace PacToolkits.Desktop.Avalonia.Common;

public sealed record LogDirectoryResolution(
    string StoredDirectory,
    string RuntimeDirectory);

public static class LogDirectory
{
    public const string LogsSegment = "logs";
    public const string CurrentSubdirectory = "desktop";

    public static LogDirectoryResolution Resolve(string? configuredDirectory)
    {
        var stored = NormalizeStoredPath(configuredDirectory);
        if (string.IsNullOrWhiteSpace(stored))
        {
            var runtime = GetDefaultDirectory();
            return new LogDirectoryResolution(string.Empty, runtime);
        }

        var configuredRuntime = ResolveRuntimePath(stored);
        return new LogDirectoryResolution(stored, configuredRuntime);
    }

    public static string GetDefaultDirectory()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(baseDir, "PacToolkits", LogsSegment, CurrentSubdirectory);
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

    private static string NormalizeStoredPath(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
