using System;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>筛选输入规范化辅助</summary>
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
