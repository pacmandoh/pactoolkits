namespace PacToolkits.Application.Services;

public static class DashboardDrugSpecParser
{
    public static bool IsInventoryAbnormalTitle(string? title)
    {
        var normalized = InputNormalizer.Normalize(title) ?? string.Empty;
        return normalized.Contains("库存", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("告紧", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("low", StringComparison.OrdinalIgnoreCase);
    }

    public static (string DrugId, string Spec)? TryParseFromAbnormalDetail(string? detail)
    {
        var s = InputNormalizer.Normalize(detail);
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }

        var head = s.Split('·', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
        if (string.IsNullOrWhiteSpace(head))
        {
            return null;
        }

        var idx = head.LastIndexOf(' ');
        if (idx <= 0 || idx >= head.Length - 1)
        {
            return null;
        }

        var drug = InputNormalizer.Normalize(head[..idx]);
        var spec = InputNormalizer.Normalize(head[(idx + 1)..]);
        if (string.IsNullOrWhiteSpace(drug) || string.IsNullOrWhiteSpace(spec))
        {
            return null;
        }

        return (drug, spec);
    }
}
