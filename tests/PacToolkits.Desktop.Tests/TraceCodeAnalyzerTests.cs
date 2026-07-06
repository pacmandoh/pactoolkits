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
        Assert.Collection(
            detailed.Lines,
            line => Assert.Equal(TraceCodeLineStatus.Valid, line.Status),
            line => Assert.Equal(TraceCodeLineStatus.Invalid, line.Status),
            line => Assert.Equal(TraceCodeLineStatus.ScanDuplicate, line.Status),
            line => Assert.Equal(TraceCodeLineStatus.PoolDuplicate, line.Status));
    }
}
