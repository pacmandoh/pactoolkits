using System;

namespace PacToolkits.Desktop.Avalonia.Ui.Formatting;

/// <summary>规格行展示格式化</summary>
public static class SpecLine
{
    public static string Format(string? drugName, string? spec)
    {
        var normalizedSpec = (spec ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalizedSpec))
        {
            return string.Empty;
        }

        var name = (drugName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            return normalizedSpec;
        }

        if (normalizedSpec.StartsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            normalizedSpec = normalizedSpec[name.Length..].TrimStart(' ', '-', '·', ':');
        }

        return normalizedSpec;
    }
}
