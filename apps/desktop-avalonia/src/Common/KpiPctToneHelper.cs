using System;

namespace PacToolkits.Desktop.Avalonia.Common;

/// <summary>
/// KPI 百分比色调规则，供 <c>StatusPill</c> 徽章与 <c>CircleProgressRing</c> 进度色共用
/// </summary>
public static class KpiPctToneHelper
{
    /// <summary>KPI 百分比口径名称常量</summary>
    public static class Metrics
    {
        public const string RemainHealth = "RemainHealth";
        public const string UsageIntensity = "UsageIntensity";
        public const string AbnormalShare = "AbnormalShare";
        public const string LowStockShare = "LowStockShare";
    }

    /// <summary>KPI 百分比色调档位</summary>
    public enum Tone
    {
        Done,
        Warning,
        Danger,
    }

    /// <summary>
    /// Scheme A 阈值：
    /// RemainHealth ≥45 / 20–44 / &lt;20；
    /// UsageIntensity ≤55 / 56–80 / &gt;80（镜像）；
    /// AbnormalShare ≤3 / 4–10 / &gt;10；
    /// LowStockShare ≤5 / 6–15 / &gt;15
    /// </summary>
    public static Tone ResolveTone(double pct, string? metric)
    {
        if (string.Equals(metric, Metrics.RemainHealth, StringComparison.OrdinalIgnoreCase))
        {
            return pct >= 45 ? Tone.Done :
                pct >= 20 ? Tone.Warning :
                Tone.Danger;
        }

        if (string.Equals(metric, Metrics.UsageIntensity, StringComparison.OrdinalIgnoreCase))
        {
            return pct <= 55 ? Tone.Done :
                pct <= 80 ? Tone.Warning :
                Tone.Danger;
        }

        if (string.Equals(metric, Metrics.AbnormalShare, StringComparison.OrdinalIgnoreCase))
        {
            return pct <= 3 ? Tone.Done :
                pct <= 10 ? Tone.Warning :
                Tone.Danger;
        }

        if (string.Equals(metric, Metrics.LowStockShare, StringComparison.OrdinalIgnoreCase))
        {
            return pct <= 5 ? Tone.Done :
                pct <= 15 ? Tone.Warning :
                Tone.Danger;
        }

        return pct <= 3 ? Tone.Done :
            pct <= 10 ? Tone.Warning :
            Tone.Danger;
    }

    public static string ToneClass(Tone tone) => tone switch
    {
        Tone.Done => "ToneDone25",
        Tone.Warning => "ToneWarning25",
        _ => "ToneDanger25",
    };

    /// <summary>色调前景/进度弧的主题资源键（与 <c>StatusPill</c> 对齐）</summary>
    public static string ProgressBrushResourceKey(Tone tone) => tone switch
    {
        Tone.Done => "SuccessColor",
        Tone.Warning => "WarningColor",
        _ => "ErrorColor",
    };

    public static string IconFor(Tone tone, string? metric)
    {
        if (string.Equals(metric, Metrics.RemainHealth, StringComparison.OrdinalIgnoreCase))
        {
            return tone switch
            {
                Tone.Done => "Package",
                Tone.Warning => "PackageOpen",
                _ => "PackageX",
            };
        }

        if (string.Equals(metric, Metrics.UsageIntensity, StringComparison.OrdinalIgnoreCase))
        {
            return tone switch
            {
                Tone.Done => "CircleDot",
                Tone.Warning => "ScanBarcode",
                _ => "Flame",
            };
        }

        if (string.Equals(metric, Metrics.AbnormalShare, StringComparison.OrdinalIgnoreCase))
        {
            return tone switch
            {
                Tone.Done => "ShieldCheck",
                Tone.Warning => "AlertTriangle",
                _ => "CircleX",
            };
        }

        if (string.Equals(metric, Metrics.LowStockShare, StringComparison.OrdinalIgnoreCase))
        {
            return tone switch
            {
                Tone.Done => "CircleCheck",
                Tone.Warning => "AlertTriangle",
                _ => "TriangleAlert",
            };
        }

        return "Percent";
    }

    public static readonly string[] ToneClasses =
    [
        "ToneDone15",
        "ToneDone25",
        "ToneWarning15",
        "ToneWarning25",
        "ToneDanger15",
        "ToneDanger25",
        "ToneInfo15",
        "ToneInfo25",
        "TonePurple15",
    ];
}
