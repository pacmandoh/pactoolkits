using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Desktop.Avalonia.Contracts;
using PacToolkits.Desktop.Avalonia.ViewModels.Pages;

namespace PacToolkits.Desktop.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    private void ScheduleAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
        _autoRefreshCts?.Dispose();
        _autoRefreshCts = new CancellationTokenSource();
        _ = RunAutoRefreshAsync(_autoRefreshCts.Token);
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
        PostOnUi(() => _ = RunWorkspaceRefreshAsync());
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

            foreach (var p in WorkspacePages)
            {
                if (!CanRefreshPage(p))
                {
                    continue;
                }

                if (!ReferenceEquals(p, active))
                {
                    MarkPageDirty(p);
                }
            }

            if (active is not null && CanRefreshPage(active))
            {
                if (await TryRefreshPageAsync(active).ConfigureAwait(true))
                {
                    ClearDirty(active);
                }
                else
                {
                    MarkPageDirty(active);
                }
            }
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
        => page is not ISettingsPage
           && page is ITopBarActions { RefreshCommand: not null };

    private static async Task<bool> TryRefreshPageAsync(AppPageBase page)
    {
        if (page is not ITopBarActions top || top.RefreshCommand is not { } cmd)
        {
            return false;
        }

        if (!cmd.CanExecute(null))
        {
            return false;
        }

        if (cmd is IAsyncRelayCommand asyncCmd)
        {
            await asyncCmd.ExecuteAsync(null).ConfigureAwait(true);
        }
        else
        {
            cmd.Execute(null);
        }

        return page.PageDataAvailability is PageDataAvailability.Ready or PageDataAvailability.Stale;
    }

    private void MarkPageDirty(AppPageBase page)
    {
        lock (_dirtyPagesGate)
        {
            _dirtyPages.Add(page);
        }
    }

    private bool IsDirty(AppPageBase page)
    {
        lock (_dirtyPagesGate)
        {
            return _dirtyPages.Contains(page);
        }
    }

    private void ClearDirty(AppPageBase page)
    {
        lock (_dirtyPagesGate)
        {
            _dirtyPages.Remove(page);
        }
    }

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
