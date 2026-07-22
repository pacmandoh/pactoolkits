using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

public sealed partial class MsfxAutoRunService
{
    private async Task PullPagesAsync(
        MsfxApiOptions options,
        RunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var begin = state.Window.BeginAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = state.Window.EndAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        for (var pass = 1; pass <= ReconciliationPasses; pass++)
        {
            var discovered = await PullPassAsync(
                options,
                state,
                observer,
                begin,
                end,
                pass,
                ct).ConfigureAwait(false);
            if (pass > 1 && discovered == 0)
            {
                break;
            }

            if (pass == ReconciliationPasses && discovered > 0)
            {
                Log(
                    observer,
                    "分页补扫",
                    $"达到 {ReconciliationPasses - 1} 次补扫上限，本轮仍发现 {discovered} 条新记录；下轮巡检将通过回看窗口继续补偿",
                    TraceEntryState.Warning);
            }
        }
    }

    private async Task<int> PullPassAsync(
        MsfxApiOptions options,
        RunState state,
        IMsfxAutoRunObserver observer,
        string begin,
        string end,
        int pass,
        CancellationToken ct)
    {
        var listedBefore = state.ListBillCodes.Count;
        var inboundBefore = state.InboundBillCodes.Count;
        var page = 1;
        var totalPages = 1;
        var phase = pass == 1 ? "上游单据" : $"分页补扫 {pass - 1}";
        var (phaseStart, phaseEnd) = GetPullPhase(pass);

        while (true)
        {
            var listedAtPageStart = state.ListBillCodes.Count;
            var inboundAtPageStart = state.InboundBillCodes.Count;
            var watchesAtPageStart = state.WatchBillCodes.Count;
            var (list, apiMilliseconds) = await GetListPageAsync(
                options,
                new MsfxListUpoutRequest(options.RefEntId, begin, end, page, PageSize),
                observer,
                ct).ConfigureAwait(false);
            state.ListApiMs += apiMilliseconds;

            if (!list.Call.Ok)
            {
                state.FailCount++;
                throw new InvalidOperationException($"上游出库单拉取失败：{BuildApiErrorMessage(list.Call)}");
            }

            if (!string.IsNullOrWhiteSpace(list.Call.RequestId))
            {
                await _store.UpdatePullBatchRequestIdAsync(
                    state.BatchId,
                    list.Call.RequestId,
                    ct).ConfigureAwait(false);
            }

            if (pass == 1)
            {
                state.ApiRows += list.Items.Count;
            }

            foreach (var item in list.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.BillCode))
                {
                    state.ListBillCodes.Add(item.BillCode);
                }
            }

            totalPages = Math.Max(1, (int)Math.Ceiling(list.Total / (double)PageSize));
            var inbound = list.Items
                .Where(item => string.Equals(item.Status, "2", StringComparison.Ordinal))
                .ToList();
            var watches = list.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.BillCode)
                    && !string.Equals(item.Status, "2", StringComparison.Ordinal))
                .GroupBy(item => item.BillCode)
                .Select(group => group.First())
                .ToList();
            if (pass == 1)
            {
                state.InboundRows += inbound.Count;
            }

            var phaseSpan = phaseEnd - phaseStart;
            var pageStart = phaseStart + phaseSpan * ((page - 1d) / totalPages);
            var pageEnd = phaseStart + phaseSpan * (page / (double)totalPages);
            Progress(
                observer,
                pageStart + ((pageEnd - pageStart) * 0.15),
                $"{phase}第 {page}/{totalPages} 页，API {list.Items.Count} 条，入库状态 {inbound.Count} 条");
            Log(observer, phase, $"第 {page} 页：API {list.Items.Count} 条，入库状态(status=2) {inbound.Count} 条", TraceEntryState.Info);

            foreach (var watch in watches)
            {
                ct.ThrowIfCancellationRequested();
                await _store.UpsertBillWatchAsync(
                    SourceApi,
                    watch.BillCode,
                    watch.FromRefUserId,
                    watch.ToRefUserId,
                    watch.FromEntName,
                    watch.BillType,
                    watch.BillTime,
                    watch.BillUploadTime,
                    watch.Status,
                    JsonSerializer.Serialize(watch),
                    ct).ConfigureAwait(false);
                if (state.WatchBillCodes.Add(watch.BillCode))
                {
                    state.WatchQueued++;
                }
            }

            var bills = inbound
                .Where(item => !string.IsNullOrWhiteSpace(item.BillCode))
                .GroupBy(item => item.BillCode)
                .Select(group => group.First())
                .ToList();
            var newBills = bills
                .Where(bill => state.InboundBillCodes.Add(bill.BillCode))
                .ToList();
            state.Bills = state.InboundBillCodes.Count;

            for (var billIndex = 0; billIndex < newBills.Count; billIndex++)
            {
                var bill = newBills[billIndex];
                ct.ThrowIfCancellationRequested();
                state.InboundVisitedBills++;
                Progress(
                    observer,
                    pageStart + ((pageEnd - pageStart) * (0.15 + (0.8 * (billIndex + 1d) / newBills.Count))),
                    $"解析单据 {state.InboundVisitedBills}/{state.Bills}：{bill.BillCode}");
                if (!state.ProcessedBillCodes.Add(bill.BillCode))
                {
                    continue;
                }

                await IngestBillAsync(
                    options,
                    state,
                    observer,
                    new BillInput(
                        bill.BillCode,
                        bill.FromRefUserId,
                        bill.FromEntName,
                        bill.ToRefUserId,
                        bill.BillType,
                        bill.BillTime,
                        bill.BillUploadTime,
                        JsonSerializer.Serialize(bill),
                        FromRetry: false,
                        FromWatch: false,
                        WatchStatus: null),
                    ct).ConfigureAwait(false);
            }

            var pageChanged = state.ListBillCodes.Count != listedAtPageStart
                || state.InboundBillCodes.Count != inboundAtPageStart
                || state.WatchBillCodes.Count != watchesAtPageStart;
            if (pass == 1 || pageChanged)
            {
                await observer.DataChangedAsync(MsfxAutoRunData.PullAudit, ct).ConfigureAwait(false);
            }

            Progress(observer, pageEnd, $"{phase}第 {page}/{totalPages} 页处理完成");
            if (list.Items.Count == 0 || page * PageSize >= list.Total)
            {
                break;
            }

            page++;
        }

        return (state.ListBillCodes.Count - listedBefore)
            + (state.InboundBillCodes.Count - inboundBefore);
    }

    private static (double Start, double End) GetPullPhase(int pass)
        => pass switch
        {
            1 => (15, 65),
            2 => (65, 70),
            _ => (70, 75)
        };

    private async Task<(MsfxListUpoutResult Result, long ApiMilliseconds)> GetListPageAsync(
        MsfxApiOptions options,
        MsfxListUpoutRequest request,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        long apiMilliseconds = 0;
        MsfxListUpoutResult? result = null;

        for (var attempt = 1; attempt <= RateLimitAttempts; attempt++)
        {
            using var timeout = CreateTimeout(options.TimeoutSeconds);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var timer = Stopwatch.StartNew();
            result = await _api.GetYljgListUpoutAsync(options, request, linked.Token).ConfigureAwait(false);
            apiMilliseconds += timer.ElapsedMilliseconds;

            if (result.Call.Ok || !IsRateLimited(result.Call) || attempt == RateLimitAttempts)
            {
                break;
            }

            var delay = TimeSpan.FromSeconds(attempt * 2);
            Log(
                observer,
                "上游限流",
                $"接口限流，{delay.TotalSeconds:F0} 秒后重试（{attempt}/{RateLimitAttempts - 1}）",
                TraceEntryState.Warning);
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }

        return (result!, apiMilliseconds);
    }

    // 上游约定：BizCode=7 / 文案含 App Call Limited
    private static bool IsRateLimited(MsfxApiCallResult call)
        => string.Equals(call.BizCode?.Trim(), "7", StringComparison.OrdinalIgnoreCase)
           || call.BizMessage?.Contains("App Call Limited", StringComparison.OrdinalIgnoreCase) == true
           || call.Summary?.Contains("App Call Limited", StringComparison.OrdinalIgnoreCase) == true;

}
