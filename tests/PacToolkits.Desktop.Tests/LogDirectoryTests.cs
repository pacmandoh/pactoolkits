using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Tests;

public sealed class LogDirectoryTests
{
    [Fact]
    public void Resolve_EmptyConfigured_UsesDefaultDesktopDirectory()
    {
        var resolution = LogDirectory.Resolve(string.Empty);

        Assert.False(resolution.RequiresMigration);
        Assert.Equal(string.Empty, resolution.StoredDirectory);
        Assert.Equal(LogDirectory.GetDefaultDirectory(), resolution.RuntimeDirectory);
    }

    [Fact]
    public void Resolve_LegacyDefaultDirectory_MigratesToDesktop()
    {
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PacToolkits",
            "logs",
            "ui");

        var resolution = LogDirectory.Resolve(legacy);

        Assert.True(resolution.RequiresMigration);
        Assert.Equal(LogDirectory.GetDefaultDirectory(), resolution.RuntimeDirectory);
        Assert.Equal(resolution.RuntimeDirectory, resolution.StoredDirectory);
        Assert.False(LogDirectory.IsLegacyLogsDirectory(resolution.StoredDirectory));
    }

    [Fact]
    public void Resolve_CustomLegacyLogsUi_MigratesSiblingDesktopDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pactoolkits-log-migrate-test");
        var legacy = Path.Combine(root, "logs", "ui");

        var resolution = LogDirectory.Resolve(legacy);

        Assert.True(resolution.RequiresMigration);
        Assert.Equal(Path.Combine(root, "logs", "desktop"), resolution.RuntimeDirectory);
        Assert.Equal(resolution.RuntimeDirectory, resolution.StoredDirectory);
    }

    [Fact]
    public void Resolve_CurrentDesktopDirectory_DoesNotMigrate()
    {
        var configured = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PacToolkits",
            "logs",
            "desktop");

        var resolution = LogDirectory.Resolve(configured);

        Assert.False(resolution.RequiresMigration);
        Assert.Equal(configured, resolution.StoredDirectory);
        Assert.Equal(Path.GetFullPath(configured), resolution.RuntimeDirectory);
    }

    [Fact]
    public void Resolve_CustomNonLegacyDirectory_DoesNotMigrate()
    {
        var configured = Path.Combine(Path.GetTempPath(), "custom-desktop-logs");

        var resolution = LogDirectory.Resolve(configured);

        Assert.False(resolution.RequiresMigration);
        Assert.Equal(configured, resolution.StoredDirectory);
        Assert.Equal(Path.GetFullPath(configured), resolution.RuntimeDirectory);
    }
}
