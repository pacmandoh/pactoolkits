using PacToolkits.Desktop.Avalonia.ViewModels;

namespace PacToolkits.Desktop.Tests;

public sealed class PageHistoryTests
{
    [Fact]
    public void Back_and_forward_follow_completed_navigation()
    {
        var history = new PageHistory<object>();
        var first = new object();
        var second = new object();
        var third = new object();

        history.Record(first, second);
        history.Record(second, third);

        Assert.Same(second, history.BackTarget);
        history.CompleteBack(third);
        Assert.Same(first, history.BackTarget);
        Assert.Same(third, history.ForwardTarget);

        history.CompleteForward(second);
        Assert.Same(second, history.BackTarget);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void New_navigation_clears_forward_history()
    {
        var history = new PageHistory<object>();
        var first = new object();
        var second = new object();
        var third = new object();
        var replacement = new object();

        history.Record(first, second);
        history.Record(second, third);
        history.CompleteBack(third);
        history.Record(second, replacement);

        Assert.Same(second, history.BackTarget);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void Initial_and_duplicate_pages_are_ignored()
    {
        var history = new PageHistory<object>();
        var page = new object();

        history.Record(null, page);
        history.Record(page, page);

        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void Equal_value_locations_are_ignored()
    {
        var history = new PageHistory<(string Page, int Tab)>();

        history.Record(("dashboard", 0), ("dashboard", 0));

        Assert.False(history.CanGoBack);
    }
}
