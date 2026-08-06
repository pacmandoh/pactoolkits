using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Services.Workspace;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    private void ScheduleAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = new CancellationTokenSource();
        ObserveDetached(RunAutoRefreshAsync(_autoRefreshCts.Token), "auto_refresh.detached.fail");
    }

    private async Task RunAutoRefreshAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_autoRefreshDebounce, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        // 页面刷新会碰绑定，放到 UI 线程执行
        PostOnUi(() => ObserveDetached(RunWorkspaceRefreshAsync(), "workspace.refresh.detached.fail"));
    }

    private bool CanWorkspaceRefresh()
        => !IsDbProbeRunning && _startupState.IsDbInitCompleted;

    private async Task RunWorkspaceRefreshAsync()
    {
        if (!CanWorkspaceRefresh())
        {
            return;
        }

        try
        {
            await _dirtyRefresh.RunAsync(WorkspacePages, ActivePage).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "page.refresh.batch_fail", "Batch refresh failed", ex);
        }
    }

    private void TryRefreshDirtyActivePage()
    {
        var active = ActivePage;
        if (active is null)
        {
            return;
        }

        _dirtyRefresh.TryRefreshIfDirty(active, () => ReferenceEquals(ActivePage, active));
    }

    private static bool CanRefreshPage(AppPageBase page)
        => WorkspacePageRefresh.CanRefreshPage(page);

    private void MarkPageDirty(AppPageBase page)
        => _dirtyRefresh.Mark(page);

    private bool IsDirty(AppPageBase page)
        => _dirtyRefresh.IsDirty(page);

    private void ClearDirty(AppPageBase page)
        => _dirtyRefresh.Clear(page);

    private void OnTopicChanged(string topic)
    {
        PostOnUi(() =>
        {
            // defer 只推迟立刻刷新；dirty 仍按 topic 标记，避免写入/编辑结束后漏刷
            var defer = WorkspaceTopicRefresh.SkipActiveRefresh(ActivePage, topic);

            ApplyTopicDirtyMarks(topic);

            if (ActivePage is DrugIndex drugIndex
                && IsDrugIndexTopic(topic))
            {
                ObserveDetached(
                    RefreshDrugIndexFromWatermarkAsync(drugIndex),
                    "drug_index.watermark.refresh.fail");
                return;
            }

            // 库存编辑中：仍 Mark dirty + defer Reload；另走静默感知，不 Discard
            if (ActivePage is InventoryOverview inventory
                && defer.Inventory
                && inventory.IsStockEditEnabled)
            {
                ObserveDetached(
                    inventory.ReconcileRemoteDuringStockEditAsync(),
                    "inventory.stock_edit.remote_reconcile_fail");
                return;
            }

            if (!defer.SkipImmediate)
            {
                TryRefreshDirtyActivePage();
            }
        });
    }

    private static bool IsDrugIndexTopic(string? topic)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();
        return key == "drug_index";
    }

    private async Task RefreshDrugIndexFromWatermarkAsync(DrugIndex page)
    {
        if (!CanWorkspaceRefresh())
        {
            return;
        }

        if (!ReferenceEquals(ActivePage, page))
        {
            return;
        }

        try
        {
            await page.ReloadFromWatermarkAsync().ConfigureAwait(true);
            if (WorkspacePageRefresh.RefreshSucceeded(page))
            {
                ClearDirty(page);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "drug_index.watermark.refresh_fail", "Drug index watermark refresh failed", ex);
        }
    }

    private void ApplyTopicDirtyMarks(string? topic)
    {
        var plan = WorkspaceTopicRefresh.PlanDirtyMarks(topic);
        WorkspaceTopicRefresh.ApplyDirtyPlan(
            plan,
            () => _lookup.InvalidateDrugCatalog(),
            type => _pageByType.TryGetValue(type, out var page) ? page : null,
            WorkspacePages,
            CanRefreshPage,
            MarkPageDirty);
    }

    private void MarkDirtyByType<TPage>() where TPage : AppPageBase
    {
        if (_pageByType.TryGetValue(typeof(TPage), out var page))
        {
            MarkPageDirty(page);
        }
    }
}
