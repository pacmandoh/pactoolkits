using System;
using System.Globalization;
using global::Avalonia.Data;
using global::Avalonia.Data.Converters;

namespace PacToolkits.Desktop.Avalonia.Converters;

/// <summary>将 DataGrid 的零基行索引转换为面向用户的一基序号</summary>
public sealed class RowIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? (i + 1).ToString() : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}

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
