using System;

namespace PacToolkits.Desktop.Avalonia.Services.Application;

public static class InventorySilentReconcilePolicy
{
    public static bool CanApply(
        int capturedEpoch,
        int currentEpoch,
        bool isPageReloadActive,
        bool isStockEditEnabled,
        bool isDetailMode,
        int capturedPage,
        int currentPageIndex,
        string? capturedKeyword,
        string? currentKeyword,
        bool isCancelled)
    {
        if (isCancelled)
        {
            return false;
        }

        if (capturedEpoch != currentEpoch || isPageReloadActive)
        {
            return false;
        }

        if (isStockEditEnabled)
        {
            return false;
        }

        if (!isDetailMode || capturedPage != currentPageIndex)
        {
            return false;
        }

        return string.Equals(capturedKeyword, currentKeyword, StringComparison.Ordinal);
    }
}
