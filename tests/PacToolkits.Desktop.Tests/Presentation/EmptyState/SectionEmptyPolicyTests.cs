using PacToolkits.Desktop.Avalonia.Contracts.Presentation;
using PacToolkits.Desktop.Avalonia.Services.Presentation.Connectivity;
using PacToolkits.Desktop.Avalonia.Services.Presentation.EmptyState;

namespace PacToolkits.Desktop.Tests;

public sealed class SectionEmptyPolicyTests
{
    [Theory]
    [InlineData(false, PageDataAvailability.NotLoaded, ConnectionKind.Unknown, false)]
    [InlineData(false, PageDataAvailability.NotLoaded, ConnectionKind.Up, true)]
    [InlineData(false, PageDataAvailability.Loading, ConnectionKind.Up, true)]
    [InlineData(false, PageDataAvailability.AwaitingService, ConnectionKind.Down, false)]
    [InlineData(false, PageDataAvailability.NotLoaded, ConnectionKind.NotConfigured, false)]
    [InlineData(false, PageDataAvailability.LoadFailed, ConnectionKind.Up, false)]
    [InlineData(true, PageDataAvailability.Loading, ConnectionKind.Up, false)]
    public void IsPending_requires_service_up_before_first_result(
        bool hasLoadedOnce,
        PageDataAvailability availability,
        ConnectionKind connection,
        bool expected)
        => Assert.Equal(expected, SectionEmptyPolicy.IsPending(hasLoadedOnce, availability, connection));
}
