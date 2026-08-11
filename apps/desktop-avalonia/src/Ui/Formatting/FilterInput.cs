using System;

namespace PacToolkits.Desktop.Avalonia.Ui.Formatting;

/// <summary>统一筛选文本的空白处理和比较形式</summary>
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
