using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Presentation;

namespace PacToolkits.Desktop.Tests;

public sealed class SectionEmptyPendingTests
{
    [Theory]
    [InlineData(PageDataAvailability.Loading)]
    [InlineData(PageDataAvailability.NotLoaded)]
    [InlineData(PageDataAvailability.AwaitingDatabase)]
    [InlineData(PageDataAvailability.AwaitingService)]
    public void Show_returns_true_during_first_fetch(PageDataAvailability availability)
    {
        Assert.True(SectionEmptyPolicy.IsPending(availability, hasLoadedOnce: false));
    }

    [Fact]
    public void Show_returns_false_after_first_successful_load()
    {
        Assert.False(SectionEmptyPolicy.IsPending(PageDataAvailability.Loading, hasLoadedOnce: true));
    }

    [Fact]
    public void Show_returns_false_when_page_is_ready()
    {
        Assert.False(SectionEmptyPolicy.IsPending(PageDataAvailability.Ready, hasLoadedOnce: false));
    }
}
