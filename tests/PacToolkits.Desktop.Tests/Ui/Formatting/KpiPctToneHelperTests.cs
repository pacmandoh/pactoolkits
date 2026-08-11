using PacToolkits.Desktop.Avalonia.Ui.Formatting;

namespace PacToolkits.Desktop.Tests;

public sealed class KpiPctToneHelperTests
{
    [Theory]
    [InlineData(45, KpiPctToneHelper.Tone.Done)]
    [InlineData(60, KpiPctToneHelper.Tone.Done)]
    [InlineData(44, KpiPctToneHelper.Tone.Warning)]
    [InlineData(20, KpiPctToneHelper.Tone.Warning)]
    [InlineData(19, KpiPctToneHelper.Tone.Danger)]
    public void RemainHealth_uses_scheme_a_thresholds(double pct, KpiPctToneHelper.Tone expected)
    {
        var tone = KpiPctToneHelper.ResolveTone(pct, KpiPctToneHelper.Metrics.RemainHealth);
        Assert.Equal(expected, tone);
    }

    [Theory]
    [InlineData(55, KpiPctToneHelper.Tone.Done)]
    [InlineData(30, KpiPctToneHelper.Tone.Done)]
    [InlineData(56, KpiPctToneHelper.Tone.Warning)]
    [InlineData(80, KpiPctToneHelper.Tone.Warning)]
    [InlineData(81, KpiPctToneHelper.Tone.Danger)]
    public void UsageIntensity_mirrors_remain_thresholds(double pct, KpiPctToneHelper.Tone expected)
    {
        var tone = KpiPctToneHelper.ResolveTone(pct, KpiPctToneHelper.Metrics.UsageIntensity);
        Assert.Equal(expected, tone);
    }

    [Theory]
    [InlineData(60, 40, KpiPctToneHelper.Tone.Done, KpiPctToneHelper.Tone.Done)]
    [InlineData(35, 65, KpiPctToneHelper.Tone.Warning, KpiPctToneHelper.Tone.Warning)]
    [InlineData(10, 90, KpiPctToneHelper.Tone.Danger, KpiPctToneHelper.Tone.Danger)]
    public void Remain_and_usage_stay_aligned(
        double remainPct,
        double usedPct,
        KpiPctToneHelper.Tone expectedRemain,
        KpiPctToneHelper.Tone expectedUsage)
    {
        Assert.Equal(expectedRemain, KpiPctToneHelper.ResolveTone(remainPct, KpiPctToneHelper.Metrics.RemainHealth));
        Assert.Equal(expectedUsage, KpiPctToneHelper.ResolveTone(usedPct, KpiPctToneHelper.Metrics.UsageIntensity));
    }

    [Theory]
    [InlineData(3, KpiPctToneHelper.Tone.Done)]
    [InlineData(4, KpiPctToneHelper.Tone.Warning)]
    [InlineData(10, KpiPctToneHelper.Tone.Warning)]
    [InlineData(11, KpiPctToneHelper.Tone.Danger)]
    public void AbnormalShare_uses_scheme_a_thresholds(double pct, KpiPctToneHelper.Tone expected)
    {
        var tone = KpiPctToneHelper.ResolveTone(pct, KpiPctToneHelper.Metrics.AbnormalShare);
        Assert.Equal(expected, tone);
    }

    [Theory]
    [InlineData(5, KpiPctToneHelper.Tone.Done)]
    [InlineData(6, KpiPctToneHelper.Tone.Warning)]
    [InlineData(15, KpiPctToneHelper.Tone.Warning)]
    [InlineData(16, KpiPctToneHelper.Tone.Danger)]
    public void LowStockShare_uses_scheme_a_thresholds(double pct, KpiPctToneHelper.Tone expected)
    {
        var tone = KpiPctToneHelper.ResolveTone(pct, KpiPctToneHelper.Metrics.LowStockShare);
        Assert.Equal(expected, tone);
    }

    [Theory]
    [InlineData(KpiPctToneHelper.Tone.Done, "SuccessColor")]
    [InlineData(KpiPctToneHelper.Tone.Warning, "WarningColor")]
    [InlineData(KpiPctToneHelper.Tone.Danger, "ErrorColor")]
    public void ProgressBrushResourceKey_matches_status_pill_foreground(KpiPctToneHelper.Tone tone, string expectedKey)
    {
        Assert.Equal(expectedKey, KpiPctToneHelper.ProgressBrushResourceKey(tone));
    }
}
