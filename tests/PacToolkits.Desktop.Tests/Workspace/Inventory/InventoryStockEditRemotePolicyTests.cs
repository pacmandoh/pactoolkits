using PacToolkits.Desktop.Avalonia.Services.Workspace.Inventory;

namespace PacToolkits.Desktop.Tests;

public sealed class InventoryStockEditRemotePolicyTests
{
    [Fact]
    public void Decide_idle_when_clean_or_dirty_without_remote_drift()
    {
        Assert.Equal(
            InventoryStockEditRemotePolicy.Action.None,
            InventoryStockEditRemotePolicy.Decide(1, "T1", 5, "T1", 5, 1));

        Assert.Equal(
            InventoryStockEditRemotePolicy.Action.None,
            InventoryStockEditRemotePolicy.Decide(1, "T1", 5, "T1", 3, 1));
    }

    [Fact]
    public void Decide_syncs_clean_row_when_remote_version_drifts()
    {
        Assert.Equal(
            InventoryStockEditRemotePolicy.Action.SyncAll,
            InventoryStockEditRemotePolicy.Decide(1, "T1", 5, "T1", 5, 2));
    }

    [Fact]
    public void Decide_baselines_when_dirty_and_remote_drifts()
    {
        Assert.Equal(
            InventoryStockEditRemotePolicy.Action.KeepDraftBaseline,
            InventoryStockEditRemotePolicy.Decide(1, "T1", 5, "T9", 5, 3));
    }

    [Fact]
    public void Decide_marks_missing_by_local_dirty()
    {
        Assert.Equal(
            InventoryStockEditRemotePolicy.Action.MissingSilent,
            InventoryStockEditRemotePolicy.Decide(1, "T1", 5, "T1", 5, null));

        Assert.Equal(
            InventoryStockEditRemotePolicy.Action.MissingNotify,
            InventoryStockEditRemotePolicy.Decide(1, "T1", 5, "T1", 2, null));
    }

    [Fact]
    public void SilentCanApply_require_edit_gate()
    {
        Assert.False(CanApply(isStockEditEnabled: true));
        Assert.True(CanApply(isStockEditEnabled: true, requireStockEditEnabled: true));
        Assert.True(CanApply(isStockEditEnabled: false));
        Assert.False(CanApply(isStockEditEnabled: false, requireStockEditEnabled: true));
    }

    private static bool CanApply(
        int capturedEpoch = 1,
        int currentEpoch = 1,
        bool isPageReloadActive = false,
        bool isStockEditEnabled = true,
        bool isDetailMode = true,
        int capturedPage = 1,
        int currentPageIndex = 1,
        string? capturedKeyword = "abc",
        string? currentKeyword = "abc",
        bool isCancelled = false,
        bool requireStockEditEnabled = false)
        => InventorySilentReconcilePolicy.CanApply(
            capturedEpoch,
            currentEpoch,
            isPageReloadActive,
            isStockEditEnabled,
            isDetailMode,
            capturedPage,
            currentPageIndex,
            capturedKeyword,
            currentKeyword,
            isCancelled,
            requireStockEditEnabled);
}
