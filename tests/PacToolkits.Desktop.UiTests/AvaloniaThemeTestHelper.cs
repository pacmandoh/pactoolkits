using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaApplication = global::Avalonia.Application;

namespace PacToolkits.Desktop.UiTests;

internal static class AvaloniaThemeTestHelper
{
    private static ResourceDictionary? _seedDictionary;

    internal static IDisposable SeedThemeScope(
        string tokenKey,
        Color tokenColor,
        string defaultOnlyTokenKey,
        Color defaultOnlyColor)
    {
        ClearSeed();
        var app = AvaloniaApplication.Current
            ?? throw new InvalidOperationException("Headless Application was not created.");

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
        return new SeedScope(ClearSeed);
    }

    private static void ClearSeed()
    {
        if (_seedDictionary is null)
        {
            return;
        }

        AvaloniaApplication.Current?.Resources.MergedDictionaries.Remove(_seedDictionary);
        _seedDictionary = null;
    }

    private sealed class SeedScope(Action clear) : IDisposable
    {
        public void Dispose() => clear();
    }
}
