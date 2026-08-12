using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;

namespace PacToolkits.Desktop.Tests;

public sealed class SectionEmptyPendingTests
{
    [Theory]
    [InlineData(PageDataAvailability.Loading)]
    public void IsPending_only_during_first_loading(PageDataAvailability availability)
    {
        Assert.True(SectionEmptyPolicy.IsPending(availability, hasLoadedOnce: false));
    }

    [Theory]
    [InlineData(PageDataAvailability.NotLoaded)]
    [InlineData(PageDataAvailability.AwaitingDatabase)]
    [InlineData(PageDataAvailability.AwaitingService)]
    public void IsPending_false_while_waiting_for_connection(PageDataAvailability availability)
    {
        Assert.False(SectionEmptyPolicy.IsPending(availability, hasLoadedOnce: false));
    }

    [Fact]
    public void IsPending_false_after_first_successful_load()
    {
        Assert.False(SectionEmptyPolicy.IsPending(PageDataAvailability.Loading, hasLoadedOnce: true));
    }

    [Fact]
    public void IsPending_false_when_page_is_ready()
    {
        Assert.False(SectionEmptyPolicy.IsPending(PageDataAvailability.Ready, hasLoadedOnce: false));
    }
}
