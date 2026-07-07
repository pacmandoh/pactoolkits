using System.Collections;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Styling;
using PacToolkits.Desktop.Avalonia.Common;
using AvaloniaApplication = global::Avalonia.Application;

namespace PacToolkits.Desktop.Tests;

[Collection("Avalonia")]
public sealed class ThemeBrushResolverTests : IDisposable
{
    private const string TokenKey = "PacTest.AccentColor";
    private const string DefaultOnlyTokenKey = "PacTest.DefaultOnlyToken";

    public ThemeBrushResolverTests()
    {
        AvaloniaTestHost.EnsureInitialized();
        AvaloniaTestHost.SeedThemeResources(
            TokenKey,
            Color.FromRgb(10, 20, 30),
            DefaultOnlyTokenKey,
            Color.FromRgb(40, 50, 60));
        ClearBrushCache();
    }

    public void Dispose()
        => ClearBrushCache();

    [Fact]
    public void GetBrush_returns_fallback_for_missing_token()
    {
        var fallback = new SolidColorBrush(Colors.Gray);

        var brush = ThemeBrushResolver.GetBrush("PacTest.MissingToken", fallback);

        Assert.Same(fallback, brush);
    }

    [Fact]
    public void GetBrush_caches_brush_per_theme_variant()
    {
        var fallback = new SolidColorBrush(Colors.Gray);
        var app = AvaloniaApplication.Current!;

        var first = ThemeBrushResolver.GetBrush(TokenKey, fallback);
        var second = ThemeBrushResolver.GetBrush(TokenKey, fallback);

        Assert.IsType<SolidColorBrush>(first);
        Assert.Same(first, second);
        Assert.Equal(Color.FromRgb(10, 20, 30), ((SolidColorBrush)first).Color);
        Assert.Equal(app.ActualThemeVariant, ResolveCachedVariant(TokenKey));
    }

    [Fact]
    public void TryGetColor_falls_back_to_default_theme_variant()
    {
        var app = AvaloniaApplication.Current!;
        app.RequestedThemeVariant = ThemeVariant.Dark;

        Assert.True(ThemeBrushResolver.TryGetColor(DefaultOnlyTokenKey, out var color));
        Assert.Equal(Color.FromRgb(40, 50, 60), color);
    }

    private static ThemeVariant ResolveCachedVariant(string key)
    {
        var cacheField = typeof(ThemeBrushResolver).GetField(
            "BrushCache",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var cache = (IDictionary)cacheField.GetValue(null)!;

        foreach (DictionaryEntry entry in cache)
        {
            if (entry.Key is ValueTuple<string, ThemeVariant> tuple && tuple.Item1 == key)
            {
                return tuple.Item2;
            }
        }

        throw new InvalidOperationException("Cache entry not found.");
    }

    private static void ClearBrushCache()
    {
        var cacheField = typeof(ThemeBrushResolver).GetField(
            "BrushCache",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var cache = (IDictionary)cacheField.GetValue(null)!;
        cache.Clear();
    }
}
