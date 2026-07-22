using PacToolkits.Application.Abstractions;

namespace PacToolkits.Application.Services;

/// <summary>
/// 应用更新通道归一与 FeedUrl 解析；比较是否有可提示更新
/// </summary>
public static class AppUpdatePolicy
{
    /// <summary>
    /// 是否存在可提示更新及说明文案
    /// </summary>
    public readonly record struct ReleaseDecision(
        bool HasUpdate,
        string Message);

    public static string NormalizeChannel(string? channel)
    {
        return TryNormalizeChannel(channel, out var normalized) ? normalized : "stable";
    }

    public static bool TryNormalizeChannel(string? channel, out string normalized)
    {
        normalized = (channel ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "stable" or "beta")
        {
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public static string ResolveFeedUrl(string? baseFeedUrl, string channel)
    {
        channel = NormalizeChannel(channel);
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
        var channel = NormalizeChannel(options.Channel);
        return $"{channel} @ {ResolveFeedUrl(options.FeedUrl, channel)}";
    }

    public static bool IsIgnored(string version, string? ignoredVersion)
        => string.Equals(version, ignoredVersion, StringComparison.Ordinal);

    public static UpdateSettingsChange GetChanges(UpdateOptions previous, UpdateOptions current)
    {
        var changes = UpdateSettingsChange.None;
        if (!string.Equals(
                NormalizeChannel(previous.Channel),
                NormalizeChannel(current.Channel),
                StringComparison.Ordinal))
        {
            changes |= UpdateSettingsChange.Channel;
        }

        if (!string.Equals(
                ResolveFeedUrl(previous.FeedUrl, "stable"),
                ResolveFeedUrl(current.FeedUrl, "stable"),
                StringComparison.OrdinalIgnoreCase))
        {
            changes |= UpdateSettingsChange.FeedUrl;
        }

        if (!string.Equals(previous.IgnoredVersion, current.IgnoredVersion, StringComparison.Ordinal))
        {
            changes |= UpdateSettingsChange.IgnoredVersion;
        }

        if (previous.AutoCheckIntervalMinutes != current.AutoCheckIntervalMinutes)
        {
            changes |= UpdateSettingsChange.PollInterval;
        }

        if (previous.AutoCheckOnStartup != current.AutoCheckOnStartup)
        {
            changes |= UpdateSettingsChange.AutoCheck;
        }

        return changes;
    }

    public static bool RequiresSourceCheck(UpdateSettingsChange changes)
        => (changes & (UpdateSettingsChange.Channel | UpdateSettingsChange.FeedUrl)) != 0;

    public static bool RequiresPollRestart(UpdateSettingsChange changes)
        => (changes & (UpdateSettingsChange.Channel
                       | UpdateSettingsChange.PollInterval
                       | UpdateSettingsChange.AutoCheck)) != 0;

    public static ReleaseDecision EvaluateRelease(string version, string? ignoredVersion)
    {
        var ignored = IsIgnored(version, ignoredVersion);
        return new ReleaseDecision(
            HasUpdate: !ignored,
            Message: ignored ? $"已忽略版本 {version}" : $"发现新版本 {version}");
    }
}
