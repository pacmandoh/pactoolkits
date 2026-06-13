using System.Text.RegularExpressions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

public static class TraceCodeAnalyzer
{
    public static CodeAnalysis Analyze(string? text, TraceCodeValidationRule rule)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CodeAnalysis(0, 0, 0, Array.Empty<string>());

        var total = 0;
        var invalid = 0;
        var duplicate = 0;
        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var code = raw.Trim();
            if (code.Length == 0)
                continue;

            total++;

            if (!IsValid(code, rule))
            {
                invalid++;
                continue;
            }

            if (!seen.Add(code))
            {
                duplicate++;
                continue;
            }

            unique.Add(code);
        }

        return new CodeAnalysis(total, invalid, duplicate, unique);
    }

    public static bool IsValid(string code, TraceCodeValidationRule rule)
    {
        if (code.Length != rule.RequiredLength)
            return false;

        if (string.IsNullOrWhiteSpace(rule.Pattern))
            return true;

        try
        {
            return Regex.IsMatch(code, rule.Pattern);
        }
        catch
        {
            return false;
        }
    }
}
