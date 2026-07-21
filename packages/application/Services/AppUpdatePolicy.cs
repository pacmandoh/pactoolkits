using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

/// <summary>
/// 应用更新通道归一与 FeedUrl 解析；比较是否有可提示更新
/// </summary>
public static class AppUpdatePolicy
{
    /// <summary>
    /// 是否存在可提示的产品/组件更新及说明文案
    /// </summary>
    public readonly record struct ReleaseDecision(
        bool HasUpdate,
        bool HasProductUpdate,
        string Message);

    public static string NormalizeChannel(string? channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim().ToLowerInvariant();
        return normalized is "stable" or "beta" ? normalized : "stable";
    }

    public static string ResolveFeedUrl(string? baseFeedUrl, string channel)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseFeedUrl)
            ? string.Empty
            : baseFeedUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedBase))
        {
            return string.Empty;
        }

        if (normalizedBase.EndsWith("/stable", StringComparison.OrdinalIgnoreCase)
            || normalizedBase.EndsWith("/beta", StringComparison.OrdinalIgnoreCase))
        {
            var lastSlash = normalizedBase.LastIndexOf('/');
            if (lastSlash > 0)
            {
                normalizedBase = normalizedBase[..lastSlash];
            }
        }

        return $"{normalizedBase}/{channel}";
    }

    public static string BuildSource(UpdateOptions options)
    {
        var channel = NormalizeFeedChannel(options.Channel);
        return $"{channel} @ {ResolveFeedUrl(options.FeedUrl, channel)}";
    }

    public static string NormalizeFeedChannel(string? channel)
        => string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim().ToLowerInvariant();

    public static bool IsIgnored(string version, string? ignoredVersion)
        => string.Equals(version, ignoredVersion, StringComparison.Ordinal);

    public static ReleaseDecision EvaluateRelease(string version, string? ignoredVersion)
    {
        var ignored = IsIgnored(version, ignoredVersion);
        return new ReleaseDecision(
            HasUpdate: !ignored,
            HasProductUpdate: !ignored,
            Message: ignored ? $"已忽略版本 {version}" : $"发现新版本 {version}");
    }
}
