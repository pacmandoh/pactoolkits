using System;

namespace PacToolkits.Desktop.Avalonia.Services.Workspace;

/// <summary>
/// 库存页静默对账门禁：epoch/reload/编辑/详情页/关键词任一不对齐则丢弃结果，避免写错行
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
