using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace pactoolkits_ui.Converters;

public sealed class StringNullOrWhiteSpaceToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
