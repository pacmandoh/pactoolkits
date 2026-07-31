using System;

namespace PacToolkits.Desktop.Avalonia.Services.Workspace;

/// <summary>
/// 验证库存静默对账结果是否仍适用于当前页面上下文，避免过期结果覆盖当前数据
/// </summary>
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
        bool isCancelled,
        bool requireStockEditEnabled = false)
    {
        if (isCancelled)
        {
            return false;
        }

        if (capturedEpoch != currentEpoch || isPageReloadActive)
        {
            return false;
        }

        if (isStockEditEnabled != requireStockEditEnabled)
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
