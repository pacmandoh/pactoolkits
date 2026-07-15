using System.Globalization;
using Avalonia.Controls;
using PacToolkits.Desktop.Avalonia.Converters;

namespace PacToolkits.Desktop.Tests;

public sealed class WindowStateConverterTests
{
    [Theory]
    [InlineData(WindowState.Normal, false)]
    [InlineData(WindowState.Minimized, false)]
    [InlineData(WindowState.Maximized, true)]
    [InlineData(WindowState.FullScreen, true)]
    public void ExpandedConverters_ShareNativeAndCustomStates(WindowState state, bool expected)
    {
        var expanded = WindowStateConverters.IsExpanded.Convert(
            state,
            typeof(bool),
            null,
            CultureInfo.InvariantCulture);
        var collapsed = WindowStateConverters.IsNotExpanded.Convert(
            state,
            typeof(bool),
            null,
            CultureInfo.InvariantCulture);

        Assert.Equal(expected, expanded);
        Assert.Equal(!expected, collapsed);
    }
}
