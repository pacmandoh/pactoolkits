using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class PageStaleWhileReconnectPolicyTests
{
    [Fact]
    public void DisconnectedAvailability_returns_stale_when_loaded_and_supported()
    {
        var availability = PageStaleWhileReconnectPolicy.DisconnectedAvailability(
            hasLoadedOnce: true,
            supportsStaleWhileReconnect: true);

        Assert.Equal(PageDataAvailability.Stale, availability);
    }

    [Fact]
    public void DisconnectedAvailability_returns_awaiting_database_before_first_load()
    {
        var availability = PageStaleWhileReconnectPolicy.DisconnectedAvailability(
            hasLoadedOnce: false,
            supportsStaleWhileReconnect: true);

        Assert.Equal(PageDataAvailability.AwaitingDatabase, availability);
    }

    [Fact]
    public void DisconnectedAvailability_returns_awaiting_database_when_stale_not_supported()
    {
        var availability = PageStaleWhileReconnectPolicy.DisconnectedAvailability(
            hasLoadedOnce: true,
            supportsStaleWhileReconnect: false);

        Assert.Equal(PageDataAvailability.AwaitingDatabase, availability);
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void ShouldSuppressReloadBusy_only_for_auto_refresh_on_stale_read_only_pages(
        bool hasLoadedOnce,
        bool supportsStaleWhileReconnect,
        bool reloadFromDbSignal,
        bool expected)
    {
        var actual = PageStaleWhileReconnectPolicy.SuppressReloadBusy(
            hasLoadedOnce,
            supportsStaleWhileReconnect,
            reloadFromDbSignal);

        Assert.Equal(expected, actual);
    }
}
