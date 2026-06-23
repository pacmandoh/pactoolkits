using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using ShadUI;

namespace PacToolkits.Desktop.Avalonia.Converters;

public static class ThemeModeConverters
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
