using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Ui.State;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class MsfxLink : AppPageBase
{
    // 整段拉取按固定页长翻页，与用户选的显示页长无关
    private const int UpoutSearchFetchSize = 100;

    private string _upoutKeyword = string.Empty;
    private string _subcodeKeyword = string.Empty;

    [RelayCommand]
    private Task QueryUpstreamAsync()
    {
        _upoutSearchDebouncer.Cancel();
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
            var begin = fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var end = toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var keyword = NormalizeText(UpstreamKeyword);
            var query = keyword is null
                ? await LoadUpoutPageAsync(options, begin, end).ConfigureAwait(false)
                : await SearchUpoutRangeAsync(options, begin, end, keyword).ConfigureAwait(false);
            if (!query.Call.Ok)
            {
                await RunOnUiAsync(() =>
                {
                    UpoutRows.Clear();
                    UpoutTotal = 0;
                    UpoutStatus = $"查询失败：{query.Call.BizCode} {query.Call.BizMessage}".Trim();
                    _toast.Error("上游出库单查询", UpoutStatus);
                });
                return;
            }

            await RunOnUiAsync(() =>
            {
                _upoutLastServerTotal = query.Total;
                _allUpoutRows = query.Rows;
                ApplyUpoutPage();
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

        await GoToUpoutPageAsync(1);
    }

    [RelayCommand]
    private async Task PrevUpoutPageAsync()
    {
        if (!HasUpoutPrevPage || IsUpoutBusy)
        {
            return;
        }

        await GoToUpoutPageAsync(UpoutPage - 1);
    }

    [RelayCommand]
    private async Task NextUpoutPageAsync()
    {
        if (!HasUpoutNextPage || IsUpoutBusy)
        {
            return;
        }

        await GoToUpoutPageAsync(UpoutPage + 1);
    }

    [RelayCommand]
    private async Task LastUpoutPageAsync()
    {
        if (!HasUpoutNextPage || IsUpoutBusy)
        {
            return;
        }

        await GoToUpoutPageAsync(UpoutTotalPages);
    }

    // 关键字命中集在本地翻页；无关键字按服务器分页重取
    private Task GoToUpoutPageAsync(int page)
    {
        UpoutPage = page;
        if (!HasActiveUpstreamSearch)
        {
            return QueryUpoutAsync(resetPage: false);
        }

        ApplyUpoutPage();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ResetUpstreamFilters()
    {
        _upoutSearchDebouncer.Cancel();
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

        _allUpoutRows.Clear();
        UpoutRows.Clear();
        UpoutTotal = 0;
        var defaults = RollingDateRangeController.Normalize(
            RollingDateRangeController.DefaultFromDate,
            RollingDateRangeController.DefaultToDate);
        UpoutFromDate = defaults.From;
        UpoutToDate = defaults.To;

        // 重置只回到初始条件并清空列表，不发查询
        _suppressUpoutQuery = true;
        try
        {
            UpstreamKeyword = string.Empty;
        }
        finally
        {
            _suppressUpoutQuery = false;
        }

        UpoutPage = 1;
        UpoutPageSize = "20";
        UpoutStatus = "筛选条件已重置";
    }

    [RelayCommand]
    private void ClearActiveUpstreamSearch()
    {
        _upoutSearchDebouncer.Cancel();

        // upout 模式下清空关键字由 OnUpstreamKeywordChanged 接着触发重查
        UpstreamKeyword = string.Empty;
        if (!IsSubcodeQueryMode)
        {
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
            ScheduleUpoutQuery();
        }
    }

    private async Task<UpoutQueryResult> LoadUpoutPageAsync(MsfxApiOptions options, string begin, string end)
    {
        var pageSize = GetPageSize();
        using var cts = CreateMsfxTimeout(options.TimeoutSeconds);
        var result = await _msfxApi.GetYljgListUpoutAsync(
            options,
            new MsfxListUpoutRequest(
                RefEntId: options.RefEntId,
                BeginDate: begin,
                EndDate: end,
                Page: UpoutPage,
                PageSize: pageSize),
            cts.Token).ConfigureAwait(false);
        if (!result.Call.Ok)
        {
            return new UpoutQueryResult(result.Call, 0, []);
        }

        var displayStart = ((UpoutPage - 1) * pageSize) + 1;
        return new UpoutQueryResult(
            result.Call,
            result.Total,
            result.Items
                .Select((item, index) => MapUpoutRow(item) with { DisplayIndex = displayStart + index })
                .ToList());
    }

    // MSFX 出库单接口没有关键字条件，只能按日期范围逐页拉完再本地匹配
    private async Task<UpoutQueryResult> SearchUpoutRangeAsync(
        MsfxApiOptions options,
        string begin,
        string end,
        string keyword)
    {
        var rows = new List<MsfxUpoutGridRow>();
        MsfxApiCallResult call;
        long total = 0;

        for (var page = 1; ; page++)
        {
            using var cts = CreateMsfxTimeout(options.TimeoutSeconds);
            var result = await _msfxApi.GetYljgListUpoutAsync(
                options,
                new MsfxListUpoutRequest(
                    RefEntId: options.RefEntId,
                    BeginDate: begin,
                    EndDate: end,
                    Page: page,
                    PageSize: UpoutSearchFetchSize),
                cts.Token).ConfigureAwait(false);
            call = result.Call;
            if (!call.Ok)
            {
                return new UpoutQueryResult(call, 0, []);
            }

            total = result.Total;
            rows.AddRange(result.Items
                .Select(MapUpoutRow)
                .Where(row => MatchUpoutKeyword(row, keyword)));
            if (result.Items.Count < UpoutSearchFetchSize || (long)page * UpoutSearchFetchSize >= total)
            {
                break;
            }
        }

        return new UpoutQueryResult(
            call,
            total,
            rows.Select((row, index) => row with { DisplayIndex = index + 1 }).ToList());
    }

    // Total 始终是服务器在日期范围内的总数，关键字查询下 Rows 只含命中行
    private sealed record UpoutQueryResult(MsfxApiCallResult Call, long Total, List<MsfxUpoutGridRow> Rows);
}
