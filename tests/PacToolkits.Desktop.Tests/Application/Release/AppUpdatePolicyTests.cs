using PacToolkits.Application.Abstractions;
using PacToolkits.Application.Services;

namespace PacToolkits.Desktop.Tests;

public sealed class AppUpdatePolicyTests
{
    [Theory]
    [InlineData(null, "stable")]
    [InlineData(" BETA ", "beta")]
    [InlineData("nightly", "stable")]
    public void Normalize_channel_preserves_supported_release_channels(string? input, string expected)
        => Assert.Equal(expected, AppUpdatePolicy.NormalizeChannel(input));

    [Fact]
    public void Feed_source_replaces_existing_channel_suffix()
    {
        var options = new UpdateOptions
        {
            Channel = "beta",
            FeedUrl = "https://updates.example/pactoolkits/current/"
        };

        Assert.Equal(
            "beta @ https://updates.example/pactoolkits/beta",
            AppUpdatePolicy.BuildSource(options));
    }

    [Fact]
    public void Feed_url_normalizes_unsupported_channel_to_stable()
        => Assert.Equal(
            "https://updates.example/pactoolkits/current",
            AppUpdatePolicy.ResolveFeedUrl("https://updates.example/pactoolkits", "nightly"));

    [Fact]
    public void Local_feed_path_combines_channel_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pactoolkits-feed-root");
        var resolved = AppUpdatePolicy.ResolveFeedUrl(root, "beta");
        Assert.Equal(Path.Combine(root.TrimEnd('/', '\\'), "beta"), resolved);
    }

    [Fact]
    public void Local_feed_strips_existing_channel_suffix()
    {
        var root = Path.Combine(Path.GetTempPath(), "pactoolkits-feed", "current");
        var resolved = AppUpdatePolicy.ResolveFeedUrl(root, "beta");
        Assert.Equal(
            Path.Combine(Path.Combine(Path.GetTempPath(), "pactoolkits-feed"), "beta"),
            resolved);
    }

    [Fact]
    public void File_uri_feed_resolves_to_local_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pactoolkits-file-uri-feed");
        var uri = new Uri(
            (dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar)
            ).AbsoluteUri.TrimEnd('/');
        Assert.True(AppUpdatePolicy.TryGetLocalFeedPath(uri, out _));
        Assert.Equal(
            Path.Combine(dir.TrimEnd('/', '\\'), "current"),
            AppUpdatePolicy.ResolveFeedUrl(uri, "stable"));
    }

    [Fact]
    public void Unc_style_feed_keeps_share_root_and_appends_channel()
    {
        const string unc = @"\\fileserver\releases\pactoolkits";
        Assert.True(AppUpdatePolicy.TryGetLocalFeedPath(unc, out _));
        Assert.Equal(
            Path.Combine(unc, "beta"),
            AppUpdatePolicy.ResolveFeedUrl(unc, "beta"));
        Assert.False(AppUpdatePolicy.TryGetLocalFeedPath("https://updates.example/feed", out _));
    }

    [Fact]
    public void Ignored_release_is_not_reported_as_update()
    {
        var decision = AppUpdatePolicy.EvaluateRelease("1.2.3", "1.2.3");

        Assert.False(decision.HasUpdate);
        Assert.Equal("已忽略版本 1.2.3", decision.Message);
    }

    [Fact]
    public void Settings_changes_are_classified_by_update_effect()
    {
        var previous = new UpdateOptions();
        var current = new UpdateOptions
        {
            AutoCheckOnStartup = false,
            Channel = "beta",
            FeedUrl = "https://mirror.example/feed/pactoolkits",
            AutoCheckIntervalMinutes = 15,
            IgnoredVersion = "1.0.0-beta.1"
        };

        var changes = AppUpdatePolicy.GetChanges(previous, current);

        Assert.True(changes.HasFlag(UpdateSettingsChange.Channel));
        Assert.True(changes.HasFlag(UpdateSettingsChange.FeedUrl));
        Assert.True(changes.HasFlag(UpdateSettingsChange.IgnoredVersion));
        Assert.True(changes.HasFlag(UpdateSettingsChange.PollInterval));
        Assert.True(changes.HasFlag(UpdateSettingsChange.AutoCheck));
        Assert.True(AppUpdatePolicy.RequiresSourceCheck(changes));
        Assert.True(AppUpdatePolicy.RequiresPollRestart(changes));
    }

    [Fact]
    public void Equivalent_feed_roots_do_not_trigger_source_change()
    {
        var previous = new UpdateOptions
        {
            Channel = "stable",
            FeedUrl = "https://updates.example/feed/pactoolkits/current/"
        };
        var current = new UpdateOptions
        {
            Channel = "stable",
            FeedUrl = "https://updates.example/feed/pactoolkits"
        };

        Assert.Equal(UpdateSettingsChange.None, AppUpdatePolicy.GetChanges(previous, current));
    }

    [Fact]
    public void Poll_only_changes_do_not_request_source_check()
    {
        var changes = UpdateSettingsChange.PollInterval | UpdateSettingsChange.AutoCheck;

        Assert.False(AppUpdatePolicy.RequiresSourceCheck(changes));
        Assert.True(AppUpdatePolicy.RequiresPollRestart(changes));
    }

    [Fact]
    public void Channel_change_rechecks_source_and_restarts_channel_default_polling()
    {
        Assert.True(AppUpdatePolicy.RequiresSourceCheck(UpdateSettingsChange.Channel));
        Assert.True(AppUpdatePolicy.RequiresPollRestart(UpdateSettingsChange.Channel));
    }

    [Fact]
    public void Feed_change_rechecks_source_without_restarting_polling()
    {
        Assert.True(AppUpdatePolicy.RequiresSourceCheck(UpdateSettingsChange.FeedUrl));
        Assert.False(AppUpdatePolicy.RequiresPollRestart(UpdateSettingsChange.FeedUrl));
    }

    [Fact]
    public void Ignored_version_change_does_not_restart_polling()
    {
        Assert.False(AppUpdatePolicy.RequiresSourceCheck(UpdateSettingsChange.IgnoredVersion));
        Assert.False(AppUpdatePolicy.RequiresPollRestart(UpdateSettingsChange.IgnoredVersion));
    }

    [Fact]
    public void Aligns_when_installed_channel_changes_against_non_empty_stamp()
    {
        var decision = AppUpdatePolicy.EvaluateChannelAlign(
            configuredChannel: "stable",
            installedChannel: "beta",
            seenInstalledChannel: "stable");

        Assert.Equal(ChannelAlignAction.AlignToInstalled, decision.Action);
        Assert.Equal("beta", decision.Channel);
        Assert.Equal("beta", decision.SeenInstalledChannel);
        Assert.Equal("beta", decision.InstalledChannel);
    }

    [Fact]
    public void Skips_align_when_installed_channel_matches_seen_stamp()
    {
        var decision = AppUpdatePolicy.EvaluateChannelAlign(
            configuredChannel: "stable",
            installedChannel: "beta",
            seenInstalledChannel: "beta");

        Assert.Equal(ChannelAlignAction.None, decision.Action);
        Assert.Equal("stable", decision.Channel);
        Assert.Equal("beta", decision.SeenInstalledChannel);
    }

    [Fact]
    public void Seeds_seen_stamp_without_align_when_channels_already_match()
    {
        var decision = AppUpdatePolicy.EvaluateChannelAlign(
            configuredChannel: "beta",
            installedChannel: "beta",
            seenInstalledChannel: string.Empty);

        Assert.Equal(ChannelAlignAction.PersistSeenOnly, decision.Action);
        Assert.Equal("beta", decision.Channel);
        Assert.Equal("beta", decision.SeenInstalledChannel);
    }

    [Fact]
    public void Asks_to_confirm_when_empty_stamp_and_channels_mismatch()
    {
        var decision = AppUpdatePolicy.EvaluateChannelAlign(
            configuredChannel: "stable",
            installedChannel: "beta",
            seenInstalledChannel: string.Empty);

        Assert.Equal(ChannelAlignAction.ConfirmMismatch, decision.Action);
        Assert.Equal("stable", decision.Channel);
        Assert.Equal("beta", decision.SeenInstalledChannel);
        Assert.Equal("beta", decision.InstalledChannel);
    }

    [Fact]
    public void Skips_align_when_installed_channel_unavailable()
    {
        var decision = AppUpdatePolicy.EvaluateChannelAlign(
            configuredChannel: "beta",
            installedChannel: string.Empty,
            seenInstalledChannel: "beta");

        Assert.Equal(ChannelAlignAction.None, decision.Action);
        Assert.Equal("beta", decision.Channel);
        Assert.Equal("beta", decision.SeenInstalledChannel);
        Assert.Equal(string.Empty, decision.InstalledChannel);
    }
}
