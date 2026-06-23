using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class DashboardViewModel : AppPageBase
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

        AbnormalPageIndex = 1;
        await ReloadAbnormalPageOnlyAsync();
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

        AbnormalPageIndex--;
        await ReloadAbnormalPageOnlyAsync();
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

        AbnormalPageIndex++;
        await ReloadAbnormalPageOnlyAsync();
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

        AbnormalPageIndex = AbnormalTotalPages;
        await ReloadAbnormalPageOnlyAsync();
    }
    private async Task ReloadAbnormalPageOnlyAsync()
    {
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsAbnormalBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.LoadAbnormalPageAsync(CurrentFilter, AbnormalPageIndex, AbnormalPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildAbnormalQueueItems(page.Rows, AbnormalPageIndex, AbnormalPageSize);
                await RunOnUiAsync(() =>
                {
                    ApplyAbnormalQueue(items, page.TotalCount);
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.abnormal_page.reload_fail", "Failed to reload abnormal page", ex);
            if (CanToastError(ex))
            {
                PostOnUi(() => _toast.Error("异常列表加载失败", ex.Message));
            }
        }
    }
}
