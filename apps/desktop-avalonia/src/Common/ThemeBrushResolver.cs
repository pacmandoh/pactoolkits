using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Styling;
using global::Avalonia.Controls;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>
/// 将 Shad 主题色 token（<see cref="Color"/> 资源）解析为 code-behind 可用的画刷
/// </summary>
public static class ThemeBrushResolver
{
    private static readonly Dictionary<(string Key, ThemeVariant Variant), SolidColorBrush> BrushCache = new();
    private static readonly object CacheLock = new();

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
    {
        var app = global::Avalonia.Application.Current;
        if (app is null)
        {
            return fallback;
        }

        var cacheKey = (key, app.ActualThemeVariant);

        lock (CacheLock)
        {
            if (BrushCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        if (!TryGetColor(key, out var color))
        {
            return fallback;
        }

        var brush = new SolidColorBrush(color);
        lock (CacheLock)
        {
            if (BrushCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            BrushCache[cacheKey] = brush;
            return brush;
        }
    }

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
