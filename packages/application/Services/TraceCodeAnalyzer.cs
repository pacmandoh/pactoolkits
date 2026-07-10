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

    public static TraceCodeAnalysisResult Analyze(
        string? text,
        TraceCodeValidationRule rule,
        IReadOnlySet<string>? existingInPool = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TraceCodeAnalysisResult(
                new TraceCodeDetailedAnalysis(0, 0, 0, 0, 0, Array.Empty<string>()),
                string.IsNullOrEmpty(text) ? [] : CreateEmptyLineKinds(text),
                Array.Empty<string>());
        }

        var lines = SplitDisplayLines(text);
        var kinds = new TraceCodeLineKind[lines.Length];
        var total = 0;
        var invalid = 0;
        var scanDuplicate = 0;
        var poolDuplicate = 0;
        var valid = 0;
        var unique = new List<string>();
        var poolCheckCandidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pool = existingInPool ?? EmptyPool;

        for (var i = 0; i < lines.Length; i++)
        {
            var code = lines[i].Trim();
            if (code.Length == 0)
            {
                kinds[i] = TraceCodeLineKind.Empty;
                continue;
            }

            total++;

            if (!IsValid(code, rule))
            {
                invalid++;
                kinds[i] = TraceCodeLineKind.Invalid;
                continue;
            }

            if (!seen.Add(code))
            {
                scanDuplicate++;
                kinds[i] = TraceCodeLineKind.ScanDuplicate;
                continue;
            }

            poolCheckCandidates.Add(code);

            if (pool.Contains(code))
            {
                poolDuplicate++;
                kinds[i] = TraceCodeLineKind.PoolDuplicate;
                continue;
            }

            valid++;
            kinds[i] = TraceCodeLineKind.Valid;
            unique.Add(code);
        }

        return new TraceCodeAnalysisResult(
            new TraceCodeDetailedAnalysis(
                total,
                invalid,
                scanDuplicate,
                poolDuplicate,
                valid,
                unique),
            kinds,
            poolCheckCandidates);
    }

    public static TraceCodeDetailedAnalysis AnalyzeDetailed(
        string? text,
        TraceCodeValidationRule rule,
        IReadOnlySet<string>? existingInPool = null)
        => Analyze(text, rule, existingInPool).Detailed;

    public static TraceCodeLineKind[] AnalyzeLineKinds(
        string? text,
        TraceCodeValidationRule rule,
        IReadOnlySet<string>? existingInPool = null)
        => Analyze(text, rule, existingInPool).LineKinds;

    public static IReadOnlyList<string> ListPoolCheckCandidates(
        string? text,
        TraceCodeValidationRule rule)
        => Analyze(text, rule, existingInPool: null).PoolCheckCandidates;

    private static TraceCodeLineKind[] CreateEmptyLineKinds(string text)
        => new TraceCodeLineKind[SplitDisplayLines(text).Length];

    private static string[] SplitDisplayLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

    private static bool IsValid(string code, TraceCodeValidationRule rule)
        => TryValidateFormat(code, rule, out _);

    private static readonly HashSet<string> EmptyPool = new(StringComparer.Ordinal);
}
