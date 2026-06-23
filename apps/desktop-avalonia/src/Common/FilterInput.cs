using System;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class FilterInput
{
    public static string? Norm(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 || string.Equals(text, "ALL", StringComparison.OrdinalIgnoreCase)
            ? null
            : text;
    }
}
