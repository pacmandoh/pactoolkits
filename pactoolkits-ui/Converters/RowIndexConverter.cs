using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace pactoolkits_ui.Converters;

public sealed class RowIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? (i + 1).ToString() : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}