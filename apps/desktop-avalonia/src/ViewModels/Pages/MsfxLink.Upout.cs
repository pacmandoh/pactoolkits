using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Common;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class MsfxLink : AppPageBase
{
    private string _upoutKeyword = string.Empty;
    private string _subcodeKeyword = string.Empty;

    [RelayCommand]
    private Task QueryUpstreamAsync()
    {
        _upoutFilterDebouncer.Cancel();
        return IsSubcodeQueryMode
            ? QuerySubCodesAsync(null, null)
            : QueryUpoutAsync(resetPage: true);
    }

    private async Task QueryUpoutAsync(bool resetPage)
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

            var displayStart = ((UpoutPage - 1) * GetPageSize()) + 1;
            var mapped = result.Items
                .Select((item, index) => MapUpoutRow(item) with { DisplayIndex = displayStart + index })
                .ToList();
            _upoutLastServerTotal = result.Total;

            await RunOnUiAsync(() =>
            {
                _allUpoutRows = mapped;
                ApplyUpoutFilter();
            });
        }
        catch (OperationCanceledException)
        {
            LogWarn("msfx.upout.query_timeout", "Upstream outbound query timed out");
            await RunOnUiAsync(() => _toast.Error("上游出库单查询", "查询超时，请稍后重试"));
        }
        catch (Exception ex)
        {
            LogError("msfx.upout.query_fail", "Failed to query upstream outbound list", ex);
            await RunOnUiAsync(() =>
            {
                UpoutStatus = $"查询异常：{ex.Message}";
                if (CanToastError(ex))
                {
                    _toast.Error("上游出库单查询", ex.Message);
                }
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
        await QueryUpoutAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task PrevUpoutPageAsync()
    {
        if (!HasUpoutPrevPage || IsUpoutBusy)
        {
            return;
        }

        UpoutPage -= 1;
        await QueryUpoutAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task NextUpoutPageAsync()
    {
        if (!HasUpoutNextPage || IsUpoutBusy)
        {
            return;
        }

        UpoutPage += 1;
        await QueryUpoutAsync(resetPage: false);
    }

    [RelayCommand]
    private async Task LastUpoutPageAsync()
    {
        if (!HasUpoutNextPage || IsUpoutBusy)
        {
            return;
        }

        UpoutPage = UpoutTotalPages;
        await QueryUpoutAsync(resetPage: false);
    }

    [RelayCommand]
    private void ResetUpstreamFilters()
    {
        _upoutFilterDebouncer.Cancel();
        if (IsSubcodeQueryMode)
        {
            _subcodeKeyword = string.Empty;
            UpstreamKeyword = string.Empty;
            _allSubCodeRows.Clear();
            SubCodeRows.Clear();
            SubcodeTotal = 0;
            SubcodePage = 1;
            SubcodePageSize = "200";
            return;
        }

        _upoutKeyword = string.Empty;
        _allUpoutRows.Clear();
        UpoutRows.Clear();
        UpoutTotal = 0;
        var defaults = RollingDateRangeController.Normalize(
            RollingDateRangeController.DefaultFromDate,
            RollingDateRangeController.DefaultToDate);
        UpoutFromDate = defaults.From;
        UpoutToDate = defaults.To;
        UpstreamKeyword = string.Empty;
        UpoutPage = 1;
        UpoutPageSize = "20";
        UpoutStatus = "筛选条件已重置";
    }

    [RelayCommand]
    private void ClearActiveUpstreamSearch()
    {
        _upoutFilterDebouncer.Cancel();
        UpstreamKeyword = string.Empty;
        if (!IsSubcodeQueryMode)
        {
            ApplyUpoutFilter();
            return;
        }

        _allSubCodeRows.Clear();
        SubCodeRows.Clear();
        SubcodeTotal = 0;
        SubcodePage = 1;
    }

    partial void OnUpstreamQueryModeChanging(int oldValue, int newValue)
    {
        if (oldValue == 0)
        {
            _upoutKeyword = UpstreamKeyword;
        }
        else
        {
            _subcodeKeyword = UpstreamKeyword;
        }
    }

    partial void OnUpstreamKeywordChanged(string value)
    {
        if (IsSubcodeQueryMode)
        {
            _subcodeKeyword = value;
        }
        else
        {
            _upoutKeyword = value;
        }

        OnPropertyChanged(nameof(HasActiveUpstreamSearch));
        if (IsUpoutQueryMode)
        {
            ScheduleUpoutFilter();
        }
    }
}
