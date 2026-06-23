using System;

namespace PacToolkits.Desktop.Avalonia.Common;

public static class DrugSpecDisplayHelper
{
    public static string NormalizeSpecLine(string? drugName, string? spec)
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
