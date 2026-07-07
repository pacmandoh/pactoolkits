using PacToolkits.Desktop.Avalonia.Services.Application;

namespace PacToolkits.Desktop.Tests;

public sealed class InventorySilentReconcilePolicyTests
{
    [Fact]
    public void CanApply_returns_true_when_all_guards_match()
    {
        Assert.True(CanApply());
    }

    [Fact]
    public void CanApply_returns_false_when_epoch_expired()
    {
        Assert.False(CanApply(currentEpoch: 2));
    }

    [Fact]
    public void CanApply_returns_false_when_page_reload_active()
    {
        Assert.False(CanApply(isPageReloadActive: true));
    }

    [Fact]
    public void CanApply_returns_false_when_stock_edit_enabled()
    {
        Assert.False(CanApply(isStockEditEnabled: true));
    }

    [Fact]
    public void CanApply_returns_false_when_not_in_detail_mode()
    {
        Assert.False(CanApply(isDetailMode: false));
    }

    [Fact]
    public void CanApply_returns_false_when_page_index_changed()
    {
        Assert.False(CanApply(currentPageIndex: 2));
    }

    [Fact]
    public void CanApply_returns_false_when_keyword_changed()
    {
        Assert.False(CanApply(currentKeyword: "other"));
    }

    [Fact]
    public void CanApply_returns_false_when_cancelled()
    {
        Assert.False(CanApply(isCancelled: true));
    }

    private static bool CanApply(
        int capturedEpoch = 1,
        int currentEpoch = 1,
        bool isPageReloadActive = false,
        bool isStockEditEnabled = false,
        bool isDetailMode = true,
        int capturedPage = 1,
        int currentPageIndex = 1,
        string? capturedKeyword = "abc",
        string? currentKeyword = "abc",
        bool isCancelled = false)
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
            isCancelled);
}
