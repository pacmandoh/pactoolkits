namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class InventoryOverview
{
    public bool ShowOpenReassign => IsDetailMode && !IsReassignOpen;
    public bool ShowCloseReassign => IsDetailMode && IsReassignOpen;
}
