using PacToolkits.Desktop.Avalonia.Contracts;

namespace PacToolkits.Desktop.Avalonia.Services.Presentation;

public static class SectionEmptyPolicy
{
    public static bool IsPending(PageDataAvailability availability, bool hasLoadedOnce)
        => !hasLoadedOnce
           && availability is PageDataAvailability.NotLoaded
               or PageDataAvailability.AwaitingDatabase
               or PageDataAvailability.Loading;

    /// <summary>
    /// Prefer the section empty-state panel over an empty grid shell once the page has settled.
    /// During the first in-flight fetch, keep the section chrome visible and let panel busy states handle loading.
    /// Gate-specific copy is handled by <see cref="SectionEmptyCopy"/>.
    /// </summary>
    public static bool Show(bool isContentEmpty, PageDataAvailability availability, bool hasLoadedOnce)
    {
        if (!isContentEmpty)
        {
            return false;
        }

        if (IsPending(availability, hasLoadedOnce))
        {
            return false;
        }

        return true;
    }
}
