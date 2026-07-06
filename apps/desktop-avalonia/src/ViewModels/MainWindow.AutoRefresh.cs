using System;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Desktop.Avalonia.Services.Application;
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

        // Reason: Refresh runs on the UI thread because page commands touch bindings.
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
            var active = ActivePage;

            await WorkspaceBatchRefresh.RunAsync(
                WorkspacePages,
                active,
                CanRefreshPage,
                TryRefreshPageAsync,
                _dirtyRefresh.Dirty).ConfigureAwait(true);
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

    private static Task<bool> TryRefreshPageAsync(AppPageBase page)
        => WorkspacePageRefresh.TryRefreshAsync(page);

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
            var skipInventoryRefresh = ActivePage is InventoryOverview inv
                                       && inv.DeferRefreshTopic(topic);
            var skipDrugIndexRefresh = ActivePage is DrugIndex drug
                                       && drug.DeferRefreshTopic(topic);

            MarkPagesDirtyByTopic(topic, skipInventoryRefresh, skipDrugIndexRefresh);

            if (ActivePage is DrugIndex drugIndex
                && IsDrugIndexTopic(topic))
            {
                ObserveDetached(
                    RefreshDrugIndexFromWatermarkAsync(drugIndex),
                    "drug_index.watermark.refresh.fail");
                return;
            }

            if (!skipInventoryRefresh
                && !skipDrugIndexRefresh)
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

    private void MarkPagesDirtyByTopic(string? topic, bool skipInventoryPage, bool skipDrugIndexPage)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();

        switch (key)
        {
            case "drug_index":
                if (!skipDrugIndexPage)
                {
                    MarkDirtyByType<DrugIndex>();
                }

                MarkDirtyByType<Dashboard>();
                MarkDirtyByType<ScanCode>();
                break;

            case "inventory":
            case "trace_pool":
            case "trace_txn":
            case "trace_txn_item":
                if (!skipInventoryPage)
                {
                    MarkDirtyByType<InventoryOverview>();
                }

                MarkDirtyByType<Dashboard>();
                break;

            case "msfx":
                MarkDirtyByType<MsfxLink>();
                break;

            default:
                foreach (var page in WorkspacePages)
                {
                    if (CanRefreshPage(page))
                    {
                        MarkPageDirty(page);
                    }
                }
                break;
        }
    }

    private void MarkDirtyByType<TPage>() where TPage : AppPageBase
    {
        if (_pageByType.TryGetValue(typeof(TPage), out var page))
        {
            MarkPageDirty(page);
        }
    }
}
