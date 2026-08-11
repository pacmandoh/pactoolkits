using PacToolkits.Core;

namespace PacToolkits.Desktop.Tests;

public sealed class SemVerTests
{
    [Theory]
    [InlineData("1.0.2-beta.8", "1.0.2", -1)]
    [InlineData("1.0.2", "1.0.2-beta.8", 1)]
    [InlineData("1.0.2", "1.0.2", 0)]
    [InlineData("1.0.3-beta.8", "1.0.3-beta.9", -1)]
    [InlineData("1.0.3-beta.9", "1.0.3-beta.8", 1)]
    [InlineData("1.0.3-beta.8", "1.0.4", -1)]
    [InlineData("1.0.4", "1.0.5", -1)]
    [InlineData("1.0.2-beta.1", "1.0.2-beta.10", -1)]
    public void Compare_follows_semver_prerelease_rules(string left, string right, int expectedSign)
    {
        var cmp = SemVer.Compare(left, right);
        Assert.Equal(Math.Sign(expectedSign), Math.Sign(cmp));
    }

    [Theory]
    // min=1.0.2 max=1.0.5（正式）
    [InlineData("1.0.2-beta.8", false)] // 低于 stable 下限
    [InlineData("1.0.2", true)]
    [InlineData("1.0.3-beta.8", true)]
    [InlineData("1.0.4", true)]
    [InlineData("1.0.5", true)]
    [InlineData("1.0.5-beta.1", true)] // 仍 <= 正式 1.0.5
    [InlineData("1.0.6-beta.1", false)]
    [InlineData("1.0.6", false)]
    public void Inclusive_range_1_0_2_to_1_0_5(string current, bool expected)
        => Assert.Equal(expected, SemVer.IsInInclusiveRange(current, "1.0.2", "1.0.5"));

    [Fact]
    public void Range_with_beta_bounds_is_ordered()
    {
        Assert.True(SemVer.IsInInclusiveRange("1.0.3-beta.5", "1.0.3-beta.1", "1.0.3-beta.8"));
        Assert.False(SemVer.IsInInclusiveRange("1.0.3-beta.9", "1.0.3-beta.1", "1.0.3-beta.8"));
        Assert.False(SemVer.IsInInclusiveRange("1.0.3", "1.0.3-beta.1", "1.0.3-beta.8"));
    }
}
