using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaApplication = global::Avalonia.Application;

namespace PacToolkits.Desktop.Tests;

internal sealed class ThemeBrushTestApp : AvaloniaApplication;

internal static class AvaloniaTestHost
{
    private static readonly Lock Gate = new();
    private static bool _initialized;
    private static ResourceDictionary? _seedDictionary;

    internal static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (_initialized)
            {
                return;
            }

            AppBuilder.Configure<ThemeBrushTestApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();

            _initialized = true;
        }
    }

    internal static void SeedThemeResources(
        string tokenKey,
        Color tokenColor,
        string defaultOnlyTokenKey,
        Color defaultOnlyColor)
    {
        EnsureInitialized();
        var app = AvaloniaApplication.Current
            ?? throw new InvalidOperationException("Headless Application was not created.");

        if (_seedDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_seedDictionary);
            _seedDictionary = null;
        }

        var defaultTheme = new ResourceDictionary
        {
            [defaultOnlyTokenKey] = defaultOnlyColor,
        };
        var darkTheme = new ResourceDictionary();

        _seedDictionary = new ResourceDictionary
        {
            [tokenKey] = tokenColor,
        };
        _seedDictionary.ThemeDictionaries.Add(ThemeVariant.Default, defaultTheme);
        _seedDictionary.ThemeDictionaries.Add(ThemeVariant.Dark, darkTheme);

        app.Resources.MergedDictionaries.Add(_seedDictionary);
    }
}
