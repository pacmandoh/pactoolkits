using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using global::Avalonia.Threading;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class Dashboard : AppPageBase
{
    [RelayCommand]
    private async Task FirstEntryPageAsync()
    {
        if (SkipTrigger("dashboard.entry.first", 180))
        {
            return;
        }

        if (!HasEntryPrevPage)
        {
            return;
        }

        await ReloadEntryPageOnlyAsync(1);
    }

    [RelayCommand]
    private async Task PrevEntryPageAsync()
    {
        if (SkipTrigger("dashboard.entry.prev", 180))
        {
            return;
        }

        if (!HasEntryPrevPage)
        {
            return;
        }

        await ReloadEntryPageOnlyAsync(EntryPageIndex - 1);
    }

    [RelayCommand]
    private async Task NextEntryPageAsync()
    {
        if (SkipTrigger("dashboard.entry.next", 180))
        {
            return;
        }

        if (!HasEntryNextPage)
        {
            return;
        }

        await ReloadEntryPageOnlyAsync(EntryPageIndex + 1);
    }

    [RelayCommand]
    private async Task LastEntryPageAsync()
    {
        if (SkipTrigger("dashboard.entry.last", 180))
        {
            return;
        }

        if (!HasEntryNextPage)
        {
            return;
        }

        await ReloadEntryPageOnlyAsync(EntryTotalPages);
    }

    private Task ReloadEntryPageOnlyAsync() => ReloadEntryPageOnlyAsync(EntryPageIndex);

    private async Task ReloadEntryPageOnlyAsync(int pageIndex)
    {
        try
        {
            await RunLocalBusyAsync(
                CancellationToken.None,
                setBusy: v => IsEntryBusy = v,
                showBusy: true,
                body: async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var page = await _dashboard.GetEntryPageAsync(CurrentFilter, pageIndex, EntryPageSize, cts.Token).ConfigureAwait(false);
                var items = BuildEntryLogsPageItems(page.Rows, pageIndex, EntryPageSize);
                await RunOnUiAsync(() =>
                {
                    EntryPageIndex = pageIndex;
                    ApplyEntryLogsPage(items, page.TotalCount);
                }, DispatcherPriority.Background);
            });
        }
        catch (Exception ex)
        {
            LogError("dashboard.entry_page.reload_fail", "Failed to reload entry page", ex);
            FinishTabReloadFail(ex, "录入列表加载失败");
        }
    }
}
