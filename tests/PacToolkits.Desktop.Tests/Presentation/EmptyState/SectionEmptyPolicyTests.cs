using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;

namespace PacToolkits.Desktop.Tests;

public sealed class SectionEmptyPolicyTests
{
    [Theory]
    [InlineData(PageDataAvailability.Ready)]
    [InlineData(PageDataAvailability.Stale)]
    [InlineData(PageDataAvailability.LoadFailed)]
    [InlineData(PageDataAvailability.AccessBlocked)]
    public void Show_returns_true_when_content_empty_and_page_has_settled(PageDataAvailability availability)
    {
        Assert.True(SectionEmptyPolicy.Show(
            isContentEmpty: true,
            availability,
            hasLoadedOnce: true));
    }

    [Theory]
    [InlineData(PageDataAvailability.Loading)]
    [InlineData(PageDataAvailability.NotLoaded)]
    [InlineData(PageDataAvailability.AwaitingDatabase)]
    [InlineData(PageDataAvailability.AwaitingService)]
    public void Show_hides_empty_state_during_first_fetch(PageDataAvailability availability)
    {
        Assert.False(SectionEmptyPolicy.Show(
            isContentEmpty: true,
            availability,
            hasLoadedOnce: false));
    }

    [Fact]
    public void Show_returns_true_during_reload_when_content_still_empty()
    {
        Assert.True(SectionEmptyPolicy.Show(
            isContentEmpty: true,
            PageDataAvailability.Loading,
            hasLoadedOnce: true));
    }

    [Fact]
    public void Show_returns_false_when_content_not_empty()
    {
        Assert.False(SectionEmptyPolicy.Show(
            isContentEmpty: false,
            PageDataAvailability.Ready,
            hasLoadedOnce: true));
    }
}
