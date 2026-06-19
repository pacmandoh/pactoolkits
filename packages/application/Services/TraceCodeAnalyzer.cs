using System.Text.RegularExpressions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

public static class TraceCodeAnalyzer
{
    public static CodeAnalysis Analyze(string? text, TraceCodeValidationRule rule)
    {
        var detailed = AnalyzeDetailed(text, rule);
        return new CodeAnalysis(
            detailed.Total,
            detailed.Invalid,
            detailed.ScanDuplicate,
            detailed.ValidUniqueCodes);
    }

    public static TraceCodeDetailedAnalysis AnalyzeDetailed(
        string? text,
        TraceCodeValidationRule rule,
        IReadOnlySet<string>? existingInPool = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TraceCodeDetailedAnalysis(
                0, 0, 0, 0, 0,
                Array.Empty<string>(),
                Array.Empty<TraceCodeLineAnalysis>());
        }

        var total = 0;
        var invalid = 0;
        var scanDuplicate = 0;
        var poolDuplicate = 0;
        var valid = 0;
        var unique = new List<string>();
        var lines = new List<TraceCodeLineAnalysis>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pool = existingInPool ?? EmptyPool;

        foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.None))
        {
            var code = raw.Trim();
            if (code.Length == 0)
            {
                lines.Add(new TraceCodeLineAnalysis(raw, string.Empty, TraceCodeLineStatus.Blank));
                continue;
            }

            total++;

            if (!IsValid(code, rule))
            {
                invalid++;
                lines.Add(new TraceCodeLineAnalysis(raw, code, TraceCodeLineStatus.Invalid));
                continue;
            }

            if (!seen.Add(code))
            {
                scanDuplicate++;
                lines.Add(new TraceCodeLineAnalysis(raw, code, TraceCodeLineStatus.ScanDuplicate));
                continue;
            }

            if (pool.Contains(code))
            {
                poolDuplicate++;
                lines.Add(new TraceCodeLineAnalysis(raw, code, TraceCodeLineStatus.PoolDuplicate));
                continue;
            }

            valid++;
            unique.Add(code);
            lines.Add(new TraceCodeLineAnalysis(raw, code, TraceCodeLineStatus.Valid));
        }

        return new TraceCodeDetailedAnalysis(
            total,
            invalid,
            scanDuplicate,
            poolDuplicate,
            valid,
            unique,
            lines);
    }

    public static bool IsValid(string code, TraceCodeValidationRule rule)
    {
        if (code.Length != rule.RequiredLength)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return true;
        }

        try
        {
            return Regex.IsMatch(code, rule.Pattern);
        }
        catch
        {
            return false;
        }
    }

    private static readonly HashSet<string> EmptyPool = new(StringComparer.Ordinal);
}
