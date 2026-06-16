namespace PacToolkits.Agent.Contracts.Agents;

public enum AgentExecutableResolutionSource
{
    Configured,
    Standard,
    Missing,
}

public sealed record AgentExecutableResolution(
    string? ResolvedPath,
    string StoredPath,
    AgentExecutableResolutionSource Source,
    bool RequiresMigration);

public static class AgentPathResolver
{
    public static AgentExecutableResolution ResolveInjectorAhk(string? configuredPath, string baseDirectory)
    {
        var configured = NormalizeStoredPath(configuredPath);
        var standardStored = AgentPaths.InjectorAhkExecutable;
        var resolvedStandard = ResolvePath(standardStored, baseDirectory);
        var standardExists = resolvedStandard is not null && File.Exists(resolvedStandard);

        if (!string.IsNullOrWhiteSpace(configured)
            && ShouldMigrateToStandard(configured)
            && standardExists)
        {
            return new AgentExecutableResolution(
                resolvedStandard,
                standardStored,
                AgentExecutableResolutionSource.Standard,
                RequiresMigration: true);
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var resolvedConfigured = ResolvePath(configured, baseDirectory);
            if (resolvedConfigured is not null && File.Exists(resolvedConfigured))
            {
                return new AgentExecutableResolution(
                    resolvedConfigured,
                    configured,
                    AgentExecutableResolutionSource.Configured,
                    RequiresMigration: false);
            }
        }

        if (standardExists)
        {
            return new AgentExecutableResolution(
                resolvedStandard,
                standardStored,
                AgentExecutableResolutionSource.Standard,
                RequiresMigration: !string.Equals(configured, standardStored, StringComparison.OrdinalIgnoreCase));
        }

        return new AgentExecutableResolution(
            null,
            string.IsNullOrWhiteSpace(configured) ? standardStored : configured,
            AgentExecutableResolutionSource.Missing,
            RequiresMigration: false);
    }

    public static bool IsLegacyInjectorStoredPath(string? storedPath)
        => MatchesStoredLayout(
            storedPath,
            AgentPaths.LegacyInjectorExecutable,
            AgentPaths.LegacyInjectorFileName,
            "Tools");

    public static bool IsPreviousStandardInjectorStoredPath(string? storedPath)
        => MatchesStoredLayout(
            storedPath,
            AgentPaths.PreviousInjectorAhkExecutable,
            AgentPaths.PreviousInjectorAhkExecutableFileName,
            "agent-injector-ahk");

    private static bool ShouldMigrateToStandard(string configured)
        => IsLegacyInjectorStoredPath(configured)
           || IsPreviousStandardInjectorStoredPath(configured);

    private static bool MatchesStoredLayout(
        string? storedPath,
        string relativeExecutable,
        string fileName,
        string expectedParentDirectory)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return false;
        }

        var normalized = storedPath.Trim().Replace('\\', Path.DirectorySeparatorChar);
        var expectedNormalized = relativeExecutable
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar);

        if (string.Equals(normalized, expectedNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            if (!string.Equals(Path.GetFileName(normalized), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var directoryName = Path.GetDirectoryName(normalized);
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                return false;
            }

            return string.Equals(
                Path.GetFileName(directoryName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                expectedParentDirectory,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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

    private static string NormalizeStoredPath(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
