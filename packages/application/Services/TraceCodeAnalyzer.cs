using System.Globalization;
using System.Text.RegularExpressions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

public static class TraceCodeAnalyzer
{
    public static bool TryValidateFormat(
        string? code,
        TraceCodeValidationRule rule,
        out string? errorMessage)
    {
        errorMessage = null;
        var trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            errorMessage = "追溯码不能为空";
            return false;
        }

        if (trimmed.Length != rule.RequiredLength)
        {
            errorMessage =
                $"追溯码长度必须为 {rule.RequiredLength.ToString(CultureInfo.InvariantCulture)} 位";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rule.Pattern))
        {
            return true;
        }

        try
        {
            if (!Regex.IsMatch(trimmed, rule.Pattern))
            {
                errorMessage = "追溯码格式不符合规则";
                return false;
            }
        }
        catch
        {
            errorMessage = "追溯码格式不符合规则";
            return false;
        }

        return true;
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

    private static bool IsValid(string code, TraceCodeValidationRule rule)
        => TryValidateFormat(code, rule, out _);

    private static readonly HashSet<string> EmptyPool = new(StringComparer.Ordinal);
}
