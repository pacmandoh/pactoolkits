using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class Dashboard : AppPageBase
{
    [RelayCommand]
    private async Task FirstAbnormalPageAsync()
    {
        if (SkipTrigger("dashboard.abnormal.first", 180))
        {
            return;
        }

        if (!HasAbnormalPrevPage)
        {
            return;
        }

        await ReloadAbnormalPageOnlyAsync(1);
    }

    [RelayCommand]
    private async Task PrevAbnormalPageAsync()
    {
        if (SkipTrigger("dashboard.abnormal.prev", 180))
        {
            return;
        }

        if (!HasAbnormalPrevPage)
        {
            return;
        }

        await ReloadAbnormalPageOnlyAsync(AbnormalPageIndex - 1);
    }

    [RelayCommand]
    private async Task NextAbnormalPageAsync()
    {
        if (SkipTrigger("dashboard.abnormal.next", 180))
        {
            return;
        }

        if (!HasAbnormalNextPage)
        {
            return;
        }

        await ReloadAbnormalPageOnlyAsync(AbnormalPageIndex + 1);
    }

    [RelayCommand]
    private async Task LastAbnormalPageAsync()
    {
        if (SkipTrigger("dashboard.abnormal.last", 180))
        {
            return;
        }

        if (!HasAbnormalNextPage)
        {
            return;
        }

        await ReloadAbnormalPageOnlyAsync(AbnormalTotalPages);
    }

    private Task ReloadAbnormalPageOnlyAsync() => ReloadAbnormalPageOnlyAsync(AbnormalPageIndex);

    private async Task ReloadAbnormalPageOnlyAsync(int pageIndex)
    {
        if (!CanPage)
        {
            return;
        }

        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsAbnormalBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.GetAbnormalPageAsync(CurrentFilter, pageIndex, AbnormalPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildAbnormalQueueItems(page.Rows, pageIndex, AbnormalPageSize);
                await RunOnUiAsync(() =>
                {
                    AbnormalPageIndex = pageIndex;
                    ApplyAbnormalQueue(items, page.TotalCount);
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.abnormal_page.reload_fail", "Failed to reload abnormal page", ex);
            FinishTabReloadFail(ex, "异常列表加载失败");
        }
    }
}
