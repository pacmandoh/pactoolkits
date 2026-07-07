using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class Dashboard : AppPageBase
{
    [RelayCommand]
    private async Task FirstTxnPageAsync()
    {
        if (SkipTrigger("dashboard.txn.first", 180))
        {
            return;
        }

        if (!HasTxnPrevPage)
        {
            return;
        }

        TxnPageIndex = 1;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevTxnPageAsync()
    {
        if (SkipTrigger("dashboard.txn.prev", 180))
        {
            return;
        }

        if (!HasTxnPrevPage)
        {
            return;
        }

        TxnPageIndex--;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextTxnPageAsync()
    {
        if (SkipTrigger("dashboard.txn.next", 180))
        {
            return;
        }

        if (!HasTxnNextPage)
        {
            return;
        }

        TxnPageIndex++;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task LastTxnPageAsync()
    {
        if (SkipTrigger("dashboard.txn.last", 180))
        {
            return;
        }

        if (!HasTxnNextPage)
        {
            return;
        }

        TxnPageIndex = TxnTotalPages;
        await ReloadTxnPageOnlyAsync();
    }

    [RelayCommand]
    private async Task FirstTxnTrendPageAsync()
    {
        if (SkipTrigger("dashboard.txntrend.first", 180))
        {
            return;
        }

        if (!HasTxnTrendPrevPage)
        {
            return;
        }

        TxnTrendPageIndex = 1;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task PrevTxnTrendPageAsync()
    {
        if (SkipTrigger("dashboard.txntrend.prev", 180))
        {
            return;
        }

        if (!HasTxnTrendPrevPage)
        {
            return;
        }

        TxnTrendPageIndex--;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task NextTxnTrendPageAsync()
    {
        if (SkipTrigger("dashboard.txntrend.next", 180))
        {
            return;
        }

        if (!HasTxnTrendNextPage)
        {
            return;
        }

        TxnTrendPageIndex++;
        await ReloadTxnTrendPageOnlyAsync();
    }

    [RelayCommand]
    private async Task LastTxnTrendPageAsync()
    {
        if (SkipTrigger("dashboard.txntrend.last", 180))
        {
            return;
        }

        if (!HasTxnTrendNextPage)
        {
            return;
        }

        TxnTrendPageIndex = TxnTrendTotalPages;
        await ReloadTxnTrendPageOnlyAsync();
    }
    private async Task ReloadTxnPageOnlyAsync()
    {
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsTxnBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.GetTxnPageAsync(CurrentFilter, TxnPageIndex, TxnPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildRecentTxnsPageItems(page.Rows, TxnPageIndex, TxnPageSize);
                await RunOnUiAsync(() =>
                {
                    ApplyRecentTxnsPage(items, page.TotalCount);
                    OnPropertyChanged(nameof(IsTxnPanelEmpty));
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.txn_page.reload_fail", "Failed to reload transaction page", ex);
            FinishTabReloadFail(ex, "事务列表加载失败");
        }
    }

    private async Task ReloadTxnTrendPageOnlyAsync()
    {
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsTxnBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.GetTxnTrendPageAsync(CurrentFilter, TxnTrendPageIndex, TxnTrendPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildTxnTrendPageItems(page.Rows);
                await RunOnUiAsync(() =>
                {
                    ApplyTxnTrendPage(items, page.TotalCount);
                    OnPropertyChanged(nameof(IsTxnPanelEmpty));
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.txn_trend.reload_fail", "Failed to reload transaction trend page", ex);
            FinishTabReloadFail(ex, "事务趋势加载失败");
        }
    }
}
