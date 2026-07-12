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
    public void Ignored_release_is_not_reported_as_product_update()
    {
        var decision = AppUpdatePolicy.EvaluateRelease("1.2.3", "1.2.3");

        Assert.False(decision.HasUpdate);
        Assert.False(decision.HasProductUpdate);
        Assert.Equal("已忽略版本 1.2.3", decision.Message);
    }

    [Fact]
    public void Feed_channel_keeps_existing_custom_value_for_adapter_compatibility()
        => Assert.Equal("nightly", AppUpdatePolicy.NormalizeFeedChannel(" Nightly "));
}
