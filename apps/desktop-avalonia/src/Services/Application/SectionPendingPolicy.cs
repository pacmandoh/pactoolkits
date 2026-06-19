using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class SectionPendingPolicy
{
    public static bool ShouldShow(PageDataAvailability availability, bool hasLoadedOnce)
        => !hasLoadedOnce
           && availability is PageDataAvailability.NotLoaded
               or PageDataAvailability.AwaitingDatabase
               or PageDataAvailability.Loading;
}
