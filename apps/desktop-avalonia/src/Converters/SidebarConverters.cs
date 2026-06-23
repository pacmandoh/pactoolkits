using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace PacToolkits.Desktop.Avalonia.Converters;

/// <summary>
/// Shad Demo sidebar pattern: expanded → no tooltip; collapsed → show label.
/// </summary>
public sealed class SidebarNavToolTipConverter : IMultiValueConverter
{
    public static readonly SidebarNavToolTipConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return null;
        }

        return values[0] is true ? null : values[1]?.ToString();
    }
}
