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

    /// <summary>
    /// 识别本地/UNC/`file://` 更新源，并归一为可给 Velopack / File API 的目录路径（不改写配置原文）
    /// </summary>
    public static bool TryGetLocalFeedPath(string? baseFeed, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(baseFeed))
        {
            return false;
        }

        var trimmed = baseFeed.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
            {
                return false;
            }

            if (uri.IsFile)
            {
                path = TrimDirSeparators(uri.LocalPath);
                return path.Length > 0;
            }

            return false;
        }

        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)
            || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            var unc = trimmed.StartsWith("//", StringComparison.Ordinal)
                ? @"\\" + trimmed[2..].Replace('/', '\\')
                : trimmed;
            path = TrimDirSeparators(unc);
            return path.Length > 0;
        }

        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        if (Path.IsPathRooted(trimmed))
        {
            path = TrimDirSeparators(trimmed);
            return path.Length > 0;
        }

        return false;
    }

    public static string ResolveFeedUrl(string? baseFeedUrl, string channel)
    {
        channel = NormalizeChannel(channel);
        var releasePointer = channel == "stable" ? "current" : "beta";
        if (string.IsNullOrWhiteSpace(baseFeedUrl))
        {
            return string.Empty;
        }

        if (TryGetLocalFeedPath(baseFeedUrl, out var localRoot))
        {
            localRoot = StripChannelSuffix(localRoot);
            return localRoot.Length == 0 ? string.Empty : Path.Combine(localRoot, releasePointer);
        }

        var normalizedBase = baseFeedUrl.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedBase))
        {
            return string.Empty;
        }

        normalizedBase = StripChannelSuffix(normalizedBase, httpStyle: true);
        return $"{normalizedBase}/{releasePointer}";
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

    public static UpdateOptions NormalizeOptions(UpdateOptions? source)
    {
        var defaults = new UpdateOptions();
        var options = source ?? new UpdateOptions();

        return new UpdateOptions
        {
            AutoCheckOnStartup = options.AutoCheckOnStartup,
            Channel = NormalizeChannel(options.Channel),
            FeedUrl = string.IsNullOrWhiteSpace(options.FeedUrl) ? defaults.FeedUrl : options.FeedUrl.Trim(),
            AutoCheckIntervalMinutes = options.AutoCheckIntervalMinutes < 0
                ? defaults.AutoCheckIntervalMinutes
                : Math.Clamp(options.AutoCheckIntervalMinutes, 0, 720),
            IgnoredVersion = (options.IgnoredVersion ?? string.Empty).Trim(),
            SeenInstalledChannel = (options.SeenInstalledChannel ?? string.Empty).Trim().ToLowerInvariant()
        };
    }

    public static UpdateOptions WithChannelState(
        UpdateOptions source,
        string channel,
        string seenInstalledChannel)
    {
        var options = NormalizeOptions(source);
        options.Channel = NormalizeChannel(channel);
        options.SeenInstalledChannel = (seenInstalledChannel ?? string.Empty).Trim().ToLowerInvariant();
        return options;
    }

    /// <summary>
    /// 评估配置通道与 Velopack 安装通道的对齐动作
    /// </summary>
    /// <remarks>
    /// 当安装通道与已确认通道不一致时，配置自动跟随安装通道
    /// 尚未记录已确认通道且配置已经一致时，仅补充确认记录
    /// 尚未记录已确认通道且配置不一致时，由界面确认是否同步，并始终记录本次确认以避免重复提示
    /// </remarks>
    public static ChannelAlignDecision EvaluateChannelAlign(
        string? configuredChannel,
        string? installedChannel,
        string? seenInstalledChannel)
    {
        var channel = NormalizeChannel(configuredChannel);
        var seen = (seenInstalledChannel ?? string.Empty).Trim().ToLowerInvariant();
        if (!TryNormalizeChannel(installedChannel, out var installed))
        {
            return new ChannelAlignDecision(ChannelAlignAction.None, channel, seen, string.Empty);
        }

        if (string.Equals(seen, installed, StringComparison.Ordinal))
        {
            return new ChannelAlignDecision(ChannelAlignAction.None, channel, seen, installed);
        }

        if (string.IsNullOrEmpty(seen))
        {
            if (string.Equals(channel, installed, StringComparison.Ordinal))
            {
                return new ChannelAlignDecision(
                    ChannelAlignAction.PersistSeenOnly,
                    channel,
                    installed,
                    installed);
            }

            return new ChannelAlignDecision(
                ChannelAlignAction.ConfirmMismatch,
                channel,
                installed,
                installed);
        }

        return new ChannelAlignDecision(
            ChannelAlignAction.AlignToInstalled,
            installed,
            installed,
            installed);
    }

    private static string StripChannelSuffix(string feedRoot, bool httpStyle = false)
    {
        if (httpStyle)
        {
            if (feedRoot.EndsWith("/current", StringComparison.OrdinalIgnoreCase)
                || feedRoot.EndsWith("/beta", StringComparison.OrdinalIgnoreCase))
            {
                var lastSlash = feedRoot.LastIndexOf('/');
                if (lastSlash > 0)
                {
                    return feedRoot[..lastSlash];
                }
            }

            return feedRoot;
        }

        foreach (var name in new[] { "current", "beta" })
        {
            foreach (var sep in new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\' })
            {
                var suffix = sep + name;
                if (feedRoot.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return TrimDirSeparators(feedRoot[..^suffix.Length]);
                }
            }
        }

        return feedRoot;
    }

    private static string TrimDirSeparators(string value)
        => value.TrimEnd('/', '\\');
}
