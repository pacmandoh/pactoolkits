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
                _dirtyPages).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warn("MainWindowVM", "page.refresh.batch_fail", "Batch refresh failed", ex);
        }
    }

    private void TryRefreshDirtyActivePage()
    {
        if (!CanWorkspaceRefresh())
        {
            return;
        }

        var active = ActivePage;
        if (active is null || !CanRefreshPage(active) || !IsDirty(active))
        {
            return;
        }

        // Defer refresh until after the sidebar/content switch paints.
        PostOnUi(async () =>
        {
            try
            {
                if (!ReferenceEquals(ActivePage, active))
                {
                    return;
                }

                if (await TryRefreshPageAsync(active).ConfigureAwait(true))
                {
                    ClearDirty(active);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn("MainWindowVM", "page.refresh.active_fail", "Active page refresh failed", ex);
            }
        });
    }

    private static bool CanRefreshPage(AppPageBase page)
        => WorkspacePageRefresh.CanRefreshPage(page);

    private static Task<bool> TryRefreshPageAsync(AppPageBase page)
        => WorkspacePageRefresh.TryRefreshAsync(page);

    private void MarkPageDirty(AppPageBase page)
        => _dirtyPages.Mark(page);

    private bool IsDirty(AppPageBase page)
        => _dirtyPages.IsDirty(page);

    private void ClearDirty(AppPageBase page)
        => _dirtyPages.Clear(page);

    private void OnTopicChanged(string topic)
    {
        PostOnUi(() =>
        {
            var skipInventoryRefresh = ActivePage is InventoryOverview inv
                                       && inv.DeferRefreshTopic(topic);
            var skipDrugIndexRefresh = ActivePage is DrugIndex drug
                                       && drug.DeferRefreshTopic(topic);

            MarkPagesDirtyByTopic(topic, skipInventoryRefresh);
            if (!skipInventoryRefresh
                && !skipDrugIndexRefresh)
            {
                TryRefreshDirtyActivePage();
            }
        });
    }

    private void MarkPagesDirtyByTopic(string? topic, bool skipInventoryPage)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();

        switch (key)
        {
            case "drug_index":
                MarkDirtyByType<DrugIndex>();
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
        if (_pageByType.TryGetValue(typeof(TPage), out var page) && CanRefreshPage(page))
        {
            MarkPageDirty(page);
        }
    }
}
