using System;
using System.Globalization;
using global::Avalonia.Data.Converters;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Ui.Formatting;

namespace PacToolkits.Desktop.Avalonia.Converters;

public sealed class HighlightFilterActiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TraceCodeHighlightFilter current)
        {
            return false;
        }

        return current == HighlightFilterKeys.FromKey(parameter as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
