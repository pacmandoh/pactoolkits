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
