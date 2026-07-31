using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Tests;

public sealed class LogDirectoryTests
{
    [Fact]
    public void Resolve_EmptyConfigured_UsesDefaultDesktopDirectory()
    {
        var resolution = LogDirectory.Resolve(string.Empty);

        Assert.Equal(string.Empty, resolution.StoredDirectory);
        Assert.Equal(LogDirectory.GetDefaultDirectory(), resolution.RuntimeDirectory);
    }

    [Fact]
    public void Resolve_ConfiguredPath_UsesStoredPathAsIs()
    {
        var configured = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PacToolkits",
            "logs",
            "ui");

        var resolution = LogDirectory.Resolve(configured);

        Assert.Equal(configured, resolution.StoredDirectory);
        Assert.Equal(Path.GetFullPath(configured), resolution.RuntimeDirectory);
    }

    [Fact]
    public void Resolve_CurrentDesktopDirectory_UsesConfiguredPath()
    {
        var configured = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PacToolkits",
            "logs",
            "desktop");

        var resolution = LogDirectory.Resolve(configured);

        Assert.Equal(configured, resolution.StoredDirectory);
        Assert.Equal(Path.GetFullPath(configured), resolution.RuntimeDirectory);
    }

    [Fact]
    public void Resolve_CustomDirectory_UsesConfiguredPath()
    {
        var configured = Path.Combine(Path.GetTempPath(), "custom-desktop-logs");

        var resolution = LogDirectory.Resolve(configured);

        Assert.Equal(configured, resolution.StoredDirectory);
        Assert.Equal(Path.GetFullPath(configured), resolution.RuntimeDirectory);
    }
}
