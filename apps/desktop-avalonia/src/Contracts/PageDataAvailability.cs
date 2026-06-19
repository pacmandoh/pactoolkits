namespace PacToolkits.Desktop.Avalonia.Contracts;

/// <summary>
/// Page content availability — separate from shell DB connectivity and section empty states.
/// </summary>
public enum PageDataAvailability
{
    NotLoaded,
    AwaitingDatabase,
    AccessBlocked,
    Loading,
    LoadFailed,
    Stale,
    Ready
}
