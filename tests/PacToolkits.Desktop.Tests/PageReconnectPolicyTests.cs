using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Presentation;

namespace PacToolkits.Desktop.Tests;

public sealed class PageReconnectPolicyTests
{
    [Fact]
    public void DisconnectedAvailability_returns_stale_when_loaded_and_supported()
    {
        var availability = PageReconnectPolicy.DisconnectedAvailability(
            hasLoadedOnce: true,
            supportsStaleWhileReconnect: true);

        Assert.Equal(PageDataAvailability.Stale, availability);
    }

    [Fact]
    public void DisconnectedAvailability_returns_awaiting_database_before_first_load()
    {
        var availability = PageReconnectPolicy.DisconnectedAvailability(
            hasLoadedOnce: false,
            supportsStaleWhileReconnect: true);

        Assert.Equal(PageDataAvailability.AwaitingDatabase, availability);
    }

    [Fact]
    public void DisconnectedAvailability_returns_awaiting_database_when_stale_not_supported()
    {
        var availability = PageReconnectPolicy.DisconnectedAvailability(
            hasLoadedOnce: true,
            supportsStaleWhileReconnect: false);

        Assert.Equal(PageDataAvailability.AwaitingDatabase, availability);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void SuppressReloadBusy_only_for_auto_refresh_on_stale_read_only_pages(
        bool hasLoadedOnce,
        bool supportsStaleWhileReconnect,
        bool reloadFromDbSignal,
        bool expected)
    {
        var actual = PageReconnectPolicy.SuppressReloadBusy(
            hasLoadedOnce,
            supportsStaleWhileReconnect,
            reloadFromDbSignal);

        Assert.Equal(expected, actual);
    }
}
