namespace PacToolkits.Core;

/// <summary>
/// 闭区间判定状态（不含 IO；读库失败等由上层映射）
/// </summary>
public enum SemVerRangeStatus
{
    Invalid,
    BelowMinimum,
    Compatible,
    AboveMaximum,
}

/// <summary>
/// 闭区间分类结果；Message 为中立技术摘要
/// </summary>
public sealed record SemVerRangeResult(
    SemVerRangeStatus Status,
    string Current,
    string Minimum,
    string Maximum,
    string Message)
{
    public bool IsCompatible => Status == SemVerRangeStatus.Compatible;
}

/// <summary>
/// SemVer 闭区间分类（Below / In / Above / Invalid）
/// </summary>
public static class SemVerRange
{
    /// <summary>
    /// 闭区间 [minimum, maximum]。allowPrerelease=false 时仅接受纯 X.Y.Z（schema 路径）
    /// </summary>
    public static SemVerRangeResult Classify(
        string? current,
        string? minimum,
        string? maximum,
        bool allowPrerelease = true)
    {
        var currentText = (current ?? string.Empty).Trim();
        var minimumText = (minimum ?? string.Empty).Trim();
        var maximumText = (maximum ?? string.Empty).Trim();

        if (!TryAccept(currentText, allowPrerelease, out var currentVersion)
            || !TryAccept(minimumText, allowPrerelease, out var minimumVersion)
            || !TryAccept(maximumText, allowPrerelease, out var maximumVersion)
            || SemVer.Compare(minimumVersion, maximumVersion) > 0)
        {
            return new SemVerRangeResult(
                SemVerRangeStatus.Invalid,
                currentText,
                minimumText,
                maximumText,
                "undetermined range");
        }

        if (SemVer.Compare(currentVersion, minimumVersion) < 0)
        {
            return new SemVerRangeResult(
                SemVerRangeStatus.BelowMinimum,
                currentText,
                minimumText,
                maximumText,
                $"version {currentText} below min {minimumText}");
        }

        if (SemVer.Compare(currentVersion, maximumVersion) > 0)
        {
            return new SemVerRangeResult(
                SemVerRangeStatus.AboveMaximum,
                currentText,
                minimumText,
                maximumText,
                $"version {currentText} above max {maximumText}");
        }

        return new SemVerRangeResult(
            SemVerRangeStatus.Compatible,
            currentText,
            minimumText,
            maximumText,
            $"version {currentText} in [{minimumText}, {maximumText}]");
    }

    private static bool TryAccept(string text, bool allowPrerelease, out SemVerInfo value)
    {
        if (!SemVer.TryParse(text, out value))
        {
            return false;
        }

        return allowPrerelease || value.PreRelease is null;
    }
}
