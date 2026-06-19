using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class SectionPendingPolicyTests
{
    [Theory]
    [InlineData(PageDataAvailability.Loading)]
    [InlineData(PageDataAvailability.NotLoaded)]
    [InlineData(PageDataAvailability.AwaitingDatabase)]
    public void ShouldShow_returns_true_during_first_fetch(PageDataAvailability availability)
    {
        Assert.True(SectionPendingPolicy.ShouldShow(availability, hasLoadedOnce: false));
    }

    [Fact]
    public void ShouldShow_returns_false_after_first_successful_load()
    {
        Assert.False(SectionPendingPolicy.ShouldShow(PageDataAvailability.Loading, hasLoadedOnce: true));
    }

    [Fact]
    public void ShouldShow_returns_false_when_page_is_ready()
    {
        Assert.False(SectionPendingPolicy.ShouldShow(PageDataAvailability.Ready, hasLoadedOnce: false));
    }
}
