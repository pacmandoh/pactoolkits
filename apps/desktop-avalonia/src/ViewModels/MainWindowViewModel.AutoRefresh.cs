using System;
using System.Threading;
using System.Threading.Tasks;
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
        PostOnUi(RefreshActiveAndMarkOthersDirty);
    }

    private void RefreshActiveAndMarkOthersDirty()
    {
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
                if (TryRefreshPage(active))
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
        var active = ActivePage;
        if (active is null || !CanRefreshPage(active) || !IsDirty(active))
        {
            return;
        }

        // Defer refresh until after the sidebar/content switch paints.
        PostOnUi(() =>
        {
            if (!ReferenceEquals(ActivePage, active))
            {
                return;
            }

            if (TryRefreshPage(active))
            {
                ClearDirty(active);
            }
        });
    }

    private static bool CanRefreshPage(AppPageBase page)
        => page is not ISettingsPage
           && page is ITopBarActions { RefreshCommand: not null };

    private static bool TryRefreshPage(AppPageBase page)
    {
        if (page is not ITopBarActions top || top.RefreshCommand is not { } cmd)
        {
            return false;
        }

        if (!cmd.CanExecute(null))
        {
            return false;
        }

        cmd.Execute(null);
        return true;
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

    private void OnWatermarkTopicChanged(string topic)
    {
        PostOnUi(() =>
        {
            var skipInventoryRefresh = ActivePage is InventoryOverviewViewModel inv
                                       && inv.ShouldDeferExternalRefreshForTopic(topic);

            MarkPagesDirtyByTopic(topic, skipInventoryRefresh);
            if (ShouldRefreshActiveImmediatelyForTopic(topic)
                && !(skipInventoryRefresh && ActivePage is InventoryOverviewViewModel))
            {
                TryRefreshDirtyActivePage();
            }
        });
    }

    private bool ShouldRefreshActiveImmediatelyForTopic(string? topic)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();
        if (ActivePage is DrugIndexViewModel)
        {
            // Drug-key migration may emit trace_pool/trace_txn topics due FK cascade.
            // Keep DrugIndex page stable (no full-page flash); defer refresh until navigation/reopen.
            if (key is "drug_index" or "inventory" or "trace_pool" or "trace_txn" or "trace_txn_item")
            {
                return false;
            }
        }

        return true;
    }

    private void MarkPagesDirtyByTopic(string? topic, bool skipInventoryPage)
    {
        var key = (topic ?? string.Empty).Trim().ToLowerInvariant();

        switch (key)
        {
            case "drug_index":
                MarkDirtyByType<DrugIndexViewModel>();
                MarkDirtyByType<DashboardViewModel>();
                break;

            case "inventory":
            case "trace_pool":
            case "trace_txn":
            case "trace_txn_item":
                if (!skipInventoryPage)
                {
                    MarkDirtyByType<InventoryOverviewViewModel>();
                }

                MarkDirtyByType<DashboardViewModel>();
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

