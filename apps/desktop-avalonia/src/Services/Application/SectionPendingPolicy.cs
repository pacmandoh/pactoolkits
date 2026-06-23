using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class SectionPendingPolicy
{
    public static bool Show(PageDataAvailability availability, bool hasLoadedOnce)
        => !hasLoadedOnce
           && availability is PageDataAvailability.NotLoaded
               or PageDataAvailability.AwaitingDatabase
               or PageDataAvailability.Loading;
}
