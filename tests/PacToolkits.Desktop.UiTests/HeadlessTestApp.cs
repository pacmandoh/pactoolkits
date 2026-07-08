using Avalonia;
using Avalonia.Headless;
using AvaloniaApplication = global::Avalonia.Application;

[assembly: AvaloniaTestApplication(typeof(PacToolkits.Desktop.UiTests.HeadlessTestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace PacToolkits.Desktop.UiTests;

internal sealed class HeadlessTestApp : AvaloniaApplication;

internal static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<HeadlessTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
