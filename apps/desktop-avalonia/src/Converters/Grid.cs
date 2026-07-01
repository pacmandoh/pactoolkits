using System;
using System.Globalization;
using global::Avalonia.Data;
using global::Avalonia.Data.Converters;

namespace PacToolkits.Desktop.Avalonia.Converters;

public sealed class RowIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? (i + 1).ToString() : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

/// <summary>Shrink qty font for long formatted numbers while keeping left-aligned layout.</summary>
public sealed class DgQtyFontSizeConverter : IValueConverter
{
    private const double BaseSize = 14;
    private const double MinSize = 9;
    private const double TargetChars = 6;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var len = (value?.ToString() ?? "").Length;
        if (len == 0)
        {
            return BaseSize;
        }

        var scale = Math.Min(1d, TargetChars / len);
        return Math.Max(MinSize, BaseSize * scale);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
