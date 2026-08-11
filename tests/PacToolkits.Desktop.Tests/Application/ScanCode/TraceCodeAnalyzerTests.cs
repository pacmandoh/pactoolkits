using PacToolkits.Application.DTOs;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class TraceCodeAnalyzerTests
{
    private static readonly TraceCodeValidationRule Rule = new(20, "^8\\d+$");

    [Fact]
    public void TryValidateFormat_rejects_empty()
    {
        Assert.False(TraceCodeAnalyzer.TryValidateFormat("  ", Rule, out var error));
        Assert.Equal("追溯码不能为空", error);
    }

    [Fact]
    public void TryValidateFormat_rejects_invalid_pattern()
    {
        Assert.False(TraceCodeAnalyzer.TryValidateFormat("short", Rule, out var error));
        Assert.Equal("追溯码长度必须为 20 位", error);
    }

    [Fact]
    public void TryValidateFormat_accepts_valid_code()
    {
        Assert.True(TraceCodeAnalyzer.TryValidateFormat("89012345678901234567", Rule, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void AnalyzeDetailed_marks_invalid_scan_duplicate_and_pool_duplicate()
    {
        const string text = """
            89012345678901234567
            short
            89012345678901234567
            89012345678901234568
            """;

        var pool = new HashSet<string>(StringComparer.Ordinal)
        {
            "89012345678901234568"
        };

        var detailed = TraceCodeAnalyzer.AnalyzeDetailed(text, Rule, pool);

        Assert.Equal(4, detailed.Total);
        Assert.Equal(1, detailed.Invalid);
        Assert.Equal(1, detailed.ScanDuplicate);
        Assert.Equal(1, detailed.PoolDuplicate);
        Assert.Equal(1, detailed.Valid);
        Assert.Equal(["89012345678901234567"], detailed.ValidUniqueCodes);
    }

    [Fact]
    public void Analyze_returns_detailed_and_line_kinds_together()
    {
        const string text = """
            89012345678901234567
            short
            89012345678901234567
            89012345678901234568
            """;

        var pool = new HashSet<string>(StringComparer.Ordinal)
        {
            "89012345678901234568"
        };

        var result = TraceCodeAnalyzer.Analyze(text, Rule, pool);

        Assert.Equal(4, result.Detailed.Total);
        Assert.Equal(1, result.Detailed.Invalid);
        Assert.Equal(1, result.Detailed.ScanDuplicate);
        Assert.Equal(1, result.Detailed.PoolDuplicate);
        Assert.Equal(1, result.Detailed.Valid);
        Assert.Equal(["89012345678901234567"], result.Detailed.ValidUniqueCodes);
        Assert.Equal(4, result.LineKinds.Length);
        Assert.Equal(["89012345678901234567", "89012345678901234568"], result.PoolCheckCandidates);
        Assert.Equal(TraceCodeLineKind.Valid, result.LineKinds[0]);
        Assert.Equal(TraceCodeLineKind.Invalid, result.LineKinds[1]);
        Assert.Equal(TraceCodeLineKind.ScanDuplicate, result.LineKinds[2]);
        Assert.Equal(TraceCodeLineKind.PoolDuplicate, result.LineKinds[3]);
    }

    [Fact]
    public void ListPoolCheckCandidates_ignores_known_pool_codes()
    {
        const string text = """
            89012345678901234567
            89012345678901234568
            """;

        var pool = new HashSet<string>(StringComparer.Ordinal)
        {
            "89012345678901234567"
        };

        var filtered = TraceCodeAnalyzer.Analyze(text, Rule, pool).Detailed.ValidUniqueCodes;
        var candidates = TraceCodeAnalyzer.ListPoolCheckCandidates(text, Rule);

        Assert.Equal(["89012345678901234568"], filtered);
        Assert.Equal(
            ["89012345678901234567", "89012345678901234568"],
            candidates);
    }

    [Fact]
    public void AnalyzeLineKinds_aligns_with_display_lines()
    {
        const string text = """
            89012345678901234567
            short
            89012345678901234567
            89012345678901234568
            """;

        var pool = new HashSet<string>(StringComparer.Ordinal)
        {
            "89012345678901234568"
        };

        var kinds = TraceCodeAnalyzer.AnalyzeLineKinds(text, Rule, pool);

        Assert.Equal(4, kinds.Length);
        Assert.Equal(TraceCodeLineKind.Valid, kinds[0]);
        Assert.Equal(TraceCodeLineKind.Invalid, kinds[1]);
        Assert.Equal(TraceCodeLineKind.ScanDuplicate, kinds[2]);
        Assert.Equal(TraceCodeLineKind.PoolDuplicate, kinds[3]);
    }
}
