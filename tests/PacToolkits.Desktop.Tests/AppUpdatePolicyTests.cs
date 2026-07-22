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
            FeedUrl = "https://updates.example/pactoolkits/stable/"
        };

        Assert.Equal(
            "beta @ https://updates.example/pactoolkits/beta",
            AppUpdatePolicy.BuildSource(options));
    }

    [Fact]
    public void Feed_url_normalizes_unsupported_channel_to_stable()
        => Assert.Equal(
            "https://updates.example/pactoolkits/stable",
            AppUpdatePolicy.ResolveFeedUrl("https://updates.example/pactoolkits", "nightly"));

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
            FeedUrl = "https://updates.example/feed/pactoolkits/stable/"
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
}
