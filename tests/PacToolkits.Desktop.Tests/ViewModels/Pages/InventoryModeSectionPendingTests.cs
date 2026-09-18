using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Tests;

public sealed class InventoryModeSectionPendingTests
{
    [Fact]
    public void Unready_active_unloaded_mode_is_not_pending()
        => Assert.False(InventoryOverview.ModeSectionPending(
            pagePending: false,
            modeBusy: false,
            modeActive: true,
            modeLoaded: false,
            canPage: false,
            mountPending: false));

    [Fact]
    public void Unready_does_not_pending_on_mount_wait()
        => Assert.False(InventoryOverview.ModeSectionPending(
            pagePending: false,
            modeBusy: false,
            modeActive: true,
            modeLoaded: true,
            canPage: false,
            mountPending: true));

    [Fact]
    public void Ready_active_unloaded_mode_is_pending()
        => Assert.True(InventoryOverview.ModeSectionPending(
            pagePending: false,
            modeBusy: false,
            modeActive: true,
            modeLoaded: false,
            canPage: true,
            mountPending: false));

    [Fact]
    public void Ready_mount_wait_is_pending()
        => Assert.True(InventoryOverview.ModeSectionPending(
            pagePending: false,
            modeBusy: false,
            modeActive: true,
            modeLoaded: true,
            canPage: true,
            mountPending: true));

    [Fact]
    public void Mode_busy_is_pending_when_ready()
        => Assert.True(InventoryOverview.ModeSectionPending(
            pagePending: false,
            modeBusy: true,
            modeActive: true,
            modeLoaded: true,
            canPage: true,
            mountPending: false));

    [Fact]
    public void Inactive_unloaded_mode_is_not_pending_when_ready()
        => Assert.False(InventoryOverview.ModeSectionPending(
            pagePending: false,
            modeBusy: false,
            modeActive: false,
            modeLoaded: false,
            canPage: true,
            mountPending: false));
}
