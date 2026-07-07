using System.Globalization;
using PacToolkits.Desktop.Avalonia.Converters;

namespace PacToolkits.Desktop.Tests;

public sealed class SidebarNavToolTipConverterTests
{
    private readonly SidebarNavToolTipConverter _converter = new();

    [Fact]
    public void Expanded_sidebar_hides_tooltip()
    {
        var result = _converter.Convert(
            [true, "药品信息维护"],
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.Null(result);
    }

    [Fact]
    public void Collapsed_sidebar_shows_label()
    {
        var result = _converter.Convert(
            [false, "药品信息维护"],
            typeof(string),
            parameter: null,
            CultureInfo.InvariantCulture);

        Assert.Equal("药品信息维护", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Too_few_values_returns_null(int valueCount)
    {
        var values = valueCount == 0
            ? Array.Empty<object?>()
            : new object?[] { false };

        var result = _converter.Convert(values, typeof(string), parameter: null, CultureInfo.InvariantCulture);

        Assert.Null(result);
    }
}
