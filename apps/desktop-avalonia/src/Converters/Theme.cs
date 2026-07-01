using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Converters;

public static class Theme
{
    private static readonly Dictionary<ThemeMode, string> IconKinds = new()
    {
        { ThemeMode.System, "Monitor" },
        { ThemeMode.Light, "Sun" },
        { ThemeMode.Dark, "Moon" },
    };

    public static readonly IValueConverter ToIconKind =
        new FuncValueConverter<ThemeMode, string>(mode =>
            IconKinds.TryGetValue(mode, out var kind) ? kind : IconKinds[ThemeMode.System]);
}

public static class WindowStateConverters
{
    public static readonly IValueConverter IsFullScreen =
        new FuncValueConverter<WindowState, bool>(state => state == WindowState.FullScreen);

    public static readonly IValueConverter IsNotFullScreen =
        new FuncValueConverter<WindowState, bool>(state => state != WindowState.FullScreen);
}

/// <summary>
/// Shad Demo sidebar pattern: expanded → no tooltip; collapsed → show label.
/// </summary>
public sealed class SidebarNavToolTipConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return null;
        }

        return values[0] is true ? null : values[1]?.ToString();
    }
}
