using PacToolkits.Core;

namespace PacToolkits.Desktop.Tests;

public sealed class SemVerRangeTests
{
    [Theory]
    [InlineData("1.2.21", SemVerRangeStatus.BelowMinimum)]
    [InlineData("1.2.22", SemVerRangeStatus.Compatible)]
    [InlineData("1.2.23", SemVerRangeStatus.AboveMaximum)]
    public void Classify_release_enforces_closed_range(
        string current,
        SemVerRangeStatus expected)
    {
        var result = SemVerRange.Classify(current, "1.2.22", "1.2.22", allowPrerelease: false);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void Above_maximum_status_and_technical_message()
    {
        var result = SemVerRange.Classify("1.2.23", "1.2.20", "1.2.22", allowPrerelease: false);

        Assert.Equal(SemVerRangeStatus.AboveMaximum, result.Status);
        Assert.Contains("above max", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Classify_rejects_leading_zeros()
    {
        var result = SemVerRange.Classify("01.2.22", "1.2.22", "1.2.22", allowPrerelease: false);
        Assert.Equal(SemVerRangeStatus.Invalid, result.Status);
    }

    [Theory]
    [InlineData("1.2.22-beta.1")]
    [InlineData("not-a-version")]
    [InlineData("")]
    public void Classify_release_invalid_when_unparsable_or_prerelease(string current)
    {
        var result = SemVerRange.Classify(current, "1.2.20", "1.2.25", allowPrerelease: false);
        Assert.Equal(SemVerRangeStatus.Invalid, result.Status);
    }

    [Fact]
    public void Classify_allows_prerelease_when_enabled()
    {
        var result = SemVerRange.Classify(
            "1.0.3-beta.5",
            "1.0.3-beta.1",
            "1.0.3-beta.8",
            allowPrerelease: true);

        Assert.Equal(SemVerRangeStatus.Compatible, result.Status);
    }

    [Fact]
    public void Classify_invalid_when_min_above_max()
    {
        var result = SemVerRange.Classify("1.2.22", "1.2.25", "1.2.20", allowPrerelease: false);
        Assert.Equal(SemVerRangeStatus.Invalid, result.Status);
    }

    [Fact]
    public void Classify_compatible_across_inclusive_span()
    {
        var result = SemVerRange.Classify("1.2.23", "1.2.20", "1.2.25", allowPrerelease: false);
        Assert.Equal(SemVerRangeStatus.Compatible, result.Status);
        Assert.True(result.IsCompatible);
    }

    [Fact]
    public void FromRange_maps_to_schema_gate_status()
    {
        var range = SemVerRange.Classify("1.2.19", "1.2.20", "1.2.25", allowPrerelease: false);
        var schema = DbSchemaCompatibilityResult.FromRange(range);

        Assert.Equal(DbSchemaCompatibility.BelowMinimum, schema.Status);
    }
}
