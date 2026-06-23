using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class MsfxLinkViewModel : AppPageBase
{
    [RelayCommand]
    private async Task QueryUpoutAsync()
    {
        _upoutFilterDebouncer.Cancel();
        await QueryUpoutCoreAsync(resetPage: true).ConfigureAwait(false);
    }

    private async Task QueryUpoutCoreAsync(bool resetPage)
    {
        if (IsUpoutBusy)
        {
            return;
        }

        if (UpoutFromDate is null || UpoutToDate is null)
        {
            _toast.Warn("上游出库单查询", "请先选择开始和结束日期");
            return;
        }

        var fromDate = UpoutFromDate.Value.Date;
        var toDate = UpoutToDate.Value.Date;
        if (fromDate > toDate)
        {
            _toast.Warn("上游出库单查询", "开始日期不能晚于结束日期");
            return;
        }

        if (resetPage)
        {
            UpoutPage = 1;
        }

        IsUpoutBusy = true;
        try
        {
            var options = BuildMsfxOptions();
            var request = new MsfxListUpoutRequest(
                RefEntId: options.RefEntId,
                BeginDate: fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                EndDate: toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Page: UpoutPage,
                PageSize: GetPageSize());

            using var cts = CreateMsfxTimeout(options.TimeoutSeconds);
            var result = await _msfxApi.GetYljgListUpoutAsync(options, request, cts.Token).ConfigureAwait(false);
            if (!result.Call.Ok)
            {
                await RunOnUiAsync(() =>
                {
                    UpoutRows.Clear();
                    UpoutTotal = 0;
                    UpoutStatus = $"查询失败：{result.Call.BizCode} {result.Call.BizMessage}".Trim();
                    _toast.Error("上游出库单查询", UpoutStatus);
                });
                return;
            }

            var mapped = result.Items.Select(MapUpoutRow).ToList();
            _upoutLastServerTotal = result.Total;

            await RunOnUiAsync(() =>
            {
                _allUpoutRows = mapped;
                ApplyUpoutFilter();
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                UpoutStatus = $"查询异常：{ex.Message}";
                _toast.Error("上游出库单查询", ex.Message);
            });
        }
        finally
        {
            await RunOnUiAsync(() => IsUpoutBusy = false);
        }
    }

    [RelayCommand]
    private async Task FirstUpoutPageAsync()
    {
        if (!HasUpoutPrevPage || IsUpoutBusy)
        {
            return;
        }

        UpoutPage = 1;
        await QueryUpoutCoreAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task PrevUpoutPageAsync()
    {
        if (!HasUpoutPrevPage || IsUpoutBusy)
        {
            return;
        }

        UpoutPage -= 1;
        await QueryUpoutCoreAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task NextUpoutPageAsync()
    {
        if (!HasUpoutNextPage || IsUpoutBusy)
        {
            return;
        }

        UpoutPage += 1;
        await QueryUpoutCoreAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task LastUpoutPageAsync()
    {
        if (!HasUpoutNextPage || IsUpoutBusy)
        {
            return;
        }

        UpoutPage = UpoutTotalPages;
        await QueryUpoutCoreAsync(resetPage: false);
    }

    [RelayCommand]
    private void ResetUpoutFilters()
    {
        _upoutFilterDebouncer.Cancel();
        _allUpoutRows.Clear();
        UpoutRows.Clear();
        UpoutTotal = 0;
        var defaults = RollingDateRangeController.Normalize(
            RollingDateRangeController.DefaultFromDate,
            RollingDateRangeController.DefaultToDate);
        UpoutFromDate = defaults.From;
        UpoutToDate = defaults.To;
        UpoutBillCodeKeyword = string.Empty;
        UpoutDrugKeyword = string.Empty;
        UpoutFromEntKeyword = string.Empty;
        UpoutPage = 1;
        UpoutPageSize = "20";
        UpoutStatus = "筛选条件已重置";
    }

    partial void OnUpoutBillCodeKeywordChanged(string value)
        => ScheduleUpoutFilter();

    partial void OnUpoutDrugKeywordChanged(string value)
        => ScheduleUpoutFilter();

    partial void OnUpoutFromEntKeywordChanged(string value)
        => ScheduleUpoutFilter();
}
