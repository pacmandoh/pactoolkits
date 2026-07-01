using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using PacToolkits.Application.DTOs;
using PacToolkits.Desktop.Avalonia.Services.Infrastructure;

namespace PacToolkits.Desktop.Avalonia.ViewModels.Pages;

public sealed partial class MsfxLink : AppPageBase
{
    [RelayCommand]
    private async Task QuerySubCodesAsync()
    {
        if (IsSubcodeBusy)
        {
            return;
        }

        var billCode = (SubcodeBillCode ?? string.Empty).Trim();
        if (billCode.Length == 0)
        {
            _toast.Warn("子码查询", "请先输入单据编码");
            return;
        }

        IsSubcodeBusy = true;
        try
        {
            var options = BuildMsfxOptions();
            var route = await ResolveBillRouteFromListUpoutAsync(options, billCode).ConfigureAwait(false);
            if (route is null || string.IsNullOrWhiteSpace(route.Value.FromRefUserId))
            {
                await RunOnUiAsync(() =>
                {
                    _allSubCodeRows.Clear();
                    SubCodeRows.Clear();
                    SubcodeTotal = 0;
                    SubcodePage = 1;
                    SubcodeStatus = "未从上游出库单查询到该单据的 from_ref_user_id";
                    _toast.Error("子码查询", SubcodeStatus);
                });
                return;
            }

            using var cts = CreateMsfxTimeout(options.TimeoutSeconds);
            var req = new MsfxListUpoutDetailRequest(
                RefEntId: options.RefEntId,
                BillCode: billCode,
                ToRefUserId: route.Value.ToRefUserId,
                FromRefUserId: route.Value.FromRefUserId);
            var detail = await _msfxApi.GetYljgListUpoutDetailAsync(options, req, cts.Token).ConfigureAwait(false);

            if (!detail.Call.Ok)
            {
                await RunOnUiAsync(() =>
                {
                    _allSubCodeRows.Clear();
                    SubCodeRows.Clear();
                    SubcodeTotal = 0;
                    SubcodePage = 1;
                    SubcodeStatus = $"查询失败：{detail.Call.BizCode} {detail.Call.BizMessage}".Trim();
                    _toast.Error("子码查询", SubcodeStatus);
                });
                return;
            }

            var rows = detail.DrugItems
                .SelectMany(drug => drug.TraceCodes.Select(code => new MsfxSubCodeGridRow(
                    BillCode: detail.BillCode,
                    DrugName: drug.PhysicName,
                    PackageSpec: drug.PackageSpec,
                    PrepnSpec: drug.PrepnSpec,
                    BatchNo: drug.ProduceBatchNo,
                    Code: code.Code,
                    Level1Code: code.Level1Code ?? string.Empty,
                    Level2Code: code.Level2Code ?? string.Empty,
                    Level3Code: code.Level3Code ?? string.Empty,
                    Level4Code: code.Level4Code ?? string.Empty,
                    Level5Code: code.Level5Code ?? string.Empty,
                    State: string.IsNullOrWhiteSpace(code.Level1Code) ? TraceEntryState.Warning : TraceEntryState.Success)))
                .ToList();

            await RunOnUiAsync(() =>
            {
                _allSubCodeRows = rows;
                SubcodeTotal = rows.Count;
                SubcodePage = 1;
                ApplySubCodePage();
                SubcodeStatus = $"子码查询完成：单据 {detail.BillCode}，共 {rows.Count} 条";
            });
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() =>
            {
                _allSubCodeRows.Clear();
                SubCodeRows.Clear();
                SubcodeTotal = 0;
                SubcodePage = 1;
                SubcodeStatus = $"查询异常：{ex.Message}";
                _toast.Error("子码查询", ex.Message);
            });
        }
        finally
        {
            await RunOnUiAsync(() => IsSubcodeBusy = false);
        }
    }

    private async Task<(string ToRefUserId, string FromRefUserId)?> ResolveBillRouteFromListUpoutAsync(
        MsfxApiOptions options,
        string billCode)
    {
        var windows = new[] { 7, 30, 90, 180, 365 };
        var end = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        const int pageSize = 100;
        const int maxPages = 30;

        foreach (var days in windows)
        {
            var begin = DateTime.Today.AddDays(-(days - 1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            for (var page = 1; page <= maxPages; page++)
            {
                using var cts = CreateMsfxTimeout(options.TimeoutSeconds);
                var list = await _msfxApi.GetYljgListUpoutAsync(
                    options,
                    new MsfxListUpoutRequest(
                        RefEntId: options.RefEntId,
                        BeginDate: begin,
                        EndDate: end,
                        Page: page,
                        PageSize: pageSize),
                    cts.Token).ConfigureAwait(false);

                if (!list.Call.Ok)
                {
                    throw new InvalidOperationException($"上游出库单查询失败：{BuildApiErrorMessage(list.Call)}");
                }

                var found = list.Items.FirstOrDefault(x => string.Equals(x.BillCode, billCode, StringComparison.OrdinalIgnoreCase));
                if (found is not null)
                {
                    var toRef = NormalizeInput(found.ToRefUserId) ?? options.RefEntId;
                    var fromRef = NormalizeInput(found.FromRefUserId);
                    return fromRef is null ? null : (toRef, fromRef);
                }

                if (list.Items.Count < pageSize)
                {
                    break;
                }
            }
        }

        return null;
    }

    [RelayCommand]
    private async Task FirstSubcodePageAsync()
    {
        if (!HasSubcodePrevPage || IsSubcodeBusy)
        {
            return;
        }

        SubcodePage = 1;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task PrevSubcodePageAsync()
    {
        if (!HasSubcodePrevPage || IsSubcodeBusy)
        {
            return;
        }

        SubcodePage -= 1;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NextSubcodePageAsync()
    {
        if (!HasSubcodeNextPage || IsSubcodeBusy)
        {
            return;
        }

        SubcodePage += 1;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task LastSubcodePageAsync()
    {
        if (!HasSubcodeNextPage || IsSubcodeBusy)
        {
            return;
        }

        SubcodePage = SubcodeTotalPages;
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void ClearSubcodeQuery()
    {
        SubcodeBillCode = string.Empty;
        _allSubCodeRows.Clear();
        SubCodeRows.Clear();
        SubcodeTotal = 0;
        SubcodePage = 1;
        SubcodeStatus = "请输入单据编码后查询子码";
    }
}
