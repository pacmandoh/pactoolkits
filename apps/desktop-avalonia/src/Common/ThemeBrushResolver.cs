using Avalonia.Media;
using Avalonia.Styling;
using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>
/// Resolves Shad theme color tokens (<see cref="Color"/> resources) to brushes for code-behind rendering.
/// </summary>
public static class ThemeBrushResolver
{
    public static bool TryGetColor(string key, out Color color)
    {
        color = default;
        var app = global::Avalonia.Application.Current;
        if (app is null)
        {
            return false;
        }

        if (TryGetColor(app, key, app.ActualThemeVariant, out color))
        {
            return true;
        }

        if (app.ActualThemeVariant != ThemeVariant.Default
            && TryGetColor(app, key, ThemeVariant.Default, out color))
        {
            return true;
        }

        return false;
    }

    public static IBrush GetBrush(string key, IBrush fallback)
        => TryGetColor(key, out var color) ? new SolidColorBrush(color) : fallback;

    private static bool TryGetColor(global::Avalonia.Application app, string key, ThemeVariant variant, out Color color)
    {
        color = default;
        if (app.TryFindResource(key, variant, out var value) && value is Color themeColor)
        {
            color = themeColor;
            return true;
        }

        return false;
    }
}
