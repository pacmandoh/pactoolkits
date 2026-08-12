using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;

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

    [Fact]
    public void ServiceUnavailableAvailability_returns_awaiting_service_before_first_load()
    {
        var availability = PageReconnectPolicy.ServiceUnavailableAvailability(
            hasLoadedOnce: false,
            supportsStaleWhileReconnect: true);

        Assert.Equal(PageDataAvailability.AwaitingService, availability);
    }

    [Fact]
    public void ServiceUnavailableAvailability_returns_stale_when_loaded_and_supported()
    {
        var availability = PageReconnectPolicy.ServiceUnavailableAvailability(
            hasLoadedOnce: true,
            supportsStaleWhileReconnect: true);

        Assert.Equal(PageDataAvailability.Stale, availability);
    }
}
