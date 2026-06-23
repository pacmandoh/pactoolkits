using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class ReleaseChannelAuthTests
{
    [Theory]
    [InlineData("stable", "stable", "")]
    [InlineData("beta", "beta", "")]
    [InlineData("stable", "stable", "beta")]
    public void Same_channel_is_always_authorized(string target, string installed, string validated)
    {
        Assert.True(ReleaseChannelAuth.IsAuthorized(target, installed, validated));
    }

    [Theory]
    [InlineData("beta", "stable", "beta")]
    [InlineData("stable", "beta", "stable")]
    public void Cross_channel_is_authorized_when_validated_matches_target(
        string target,
        string installed,
        string validated)
    {
        Assert.True(ReleaseChannelAuth.IsAuthorized(target, installed, validated));
    }

    [Theory]
    [InlineData("beta", "stable", "")]
    [InlineData("beta", "stable", "stable")]
    [InlineData("stable", "beta", "")]
    [InlineData("stable", "beta", "beta")]
    public void Cross_channel_is_blocked_without_matching_validation(
        string target,
        string installed,
        string validated)
    {
        Assert.False(ReleaseChannelAuth.IsAuthorized(target, installed, validated));
    }

    [Fact]
    public void Unknown_installed_channel_is_authorized_for_dev_runs()
    {
        Assert.True(ReleaseChannelAuth.IsAuthorized("beta", "", ""));
    }

    [Fact]
    public void BuildBlockedMessage_mentions_channel_switch_validation()
    {
        var message = ReleaseChannelAuth.BuildBlockedMessage("stable", "beta");

        Assert.Contains("stable", message, StringComparison.Ordinal);
        Assert.Contains("beta", message, StringComparison.Ordinal);
        Assert.Contains("设置", message, StringComparison.Ordinal);
    }
}
