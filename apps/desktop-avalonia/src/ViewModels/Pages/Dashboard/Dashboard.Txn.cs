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

        await ReloadTxnPageOnlyAsync(1);
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

        await ReloadTxnPageOnlyAsync(TxnPageIndex - 1);
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

        await ReloadTxnPageOnlyAsync(TxnPageIndex + 1);
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

        await ReloadTxnPageOnlyAsync(TxnTotalPages);
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

        await ReloadTxnTrendPageOnlyAsync(1);
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

        await ReloadTxnTrendPageOnlyAsync(TxnTrendPageIndex - 1);
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

        await ReloadTxnTrendPageOnlyAsync(TxnTrendPageIndex + 1);
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

        await ReloadTxnTrendPageOnlyAsync(TxnTrendTotalPages);
    }

    private Task ReloadTxnPageOnlyAsync() => ReloadTxnPageOnlyAsync(TxnPageIndex);

    private async Task ReloadTxnPageOnlyAsync(int pageIndex)
    {
        if (!CanPage)
        {
            return;
        }

        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsTxnBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.GetTxnPageAsync(CurrentFilter, pageIndex, TxnPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildRecentTxnsPageItems(page.Rows, pageIndex, TxnPageSize);
                await RunOnUiAsync(() =>
                {
                    TxnPageIndex = pageIndex;
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

    private Task ReloadTxnTrendPageOnlyAsync() => ReloadTxnTrendPageOnlyAsync(TxnTrendPageIndex);

    private async Task ReloadTxnTrendPageOnlyAsync(int pageIndex)
    {
        if (!CanPage)
        {
            return;
        }

        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsTxnBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.GetTxnTrendPageAsync(CurrentFilter, pageIndex, TxnTrendPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildTxnTrendPageItems(page.Rows);
                await RunOnUiAsync(() =>
                {
                    TxnTrendPageIndex = pageIndex;
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
