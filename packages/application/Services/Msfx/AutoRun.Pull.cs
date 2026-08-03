using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

/// <summary>
/// 自动巡检：列表拉取、重试/观察与入库
/// </summary>
internal sealed class AutoRunPull
{
    private readonly IMsfxApiClient _api;
    private readonly IMsfxPullRepo _pull;
    private readonly IMsfxIngestRepo _ingest;

    public AutoRunPull(IMsfxApiClient api, IMsfxPullRepo pull, IMsfxIngestRepo ingest)
    {
        _api = api;
        _pull = pull;
        _ingest = ingest;
    }

    public async Task PullPagesAsync(
        MsfxApiOptions options,
        AutoRunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var begin = state.Window.BeginAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = state.Window.EndAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        for (var pass = 1; pass <= AutoRunLimits.ReconciliationPasses; pass++)
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

            if (pass == AutoRunLimits.ReconciliationPasses && discovered > 0)
            {
                AutoRunLog.Log(
                    observer,
                    "分页补扫",
                    $"达到 {AutoRunLimits.ReconciliationPasses - 1} 次补扫上限，本轮仍发现 {discovered} 条新记录；下轮巡检将通过回看窗口继续补偿",
                    TraceEntryState.Warning);
            }
        }
    }

    private async Task<int> PullPassAsync(
        MsfxApiOptions options,
        AutoRunState state,
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
                new MsfxListUpoutRequest(options.RefEntId, begin, end, page, AutoRunLimits.PageSize),
                observer,
                ct).ConfigureAwait(false);
            state.ListApiMs += apiMilliseconds;

            if (!list.Call.Ok)
            {
                state.FailCount++;
                throw new InvalidOperationException($"上游出库单拉取失败：{AutoRunLog.BuildApiErrorMessage(list.Call)}");
            }

            if (!string.IsNullOrWhiteSpace(list.Call.RequestId))
            {
                await _pull.UpdatePullBatchRequestIdAsync(
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

            totalPages = Math.Max(1, (int)Math.Ceiling(list.Total / (double)AutoRunLimits.PageSize));
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
            AutoRunLog.Progress(
                observer,
                pageStart + ((pageEnd - pageStart) * 0.15),
                $"{phase}第 {page}/{totalPages} 页，API {list.Items.Count} 条，入库状态 {inbound.Count} 条");
            AutoRunLog.Log(observer, phase, $"第 {page} 页：API {list.Items.Count} 条，入库状态(status=2) {inbound.Count} 条", TraceEntryState.Info);

            foreach (var watch in watches)
            {
                ct.ThrowIfCancellationRequested();
                await _pull.UpsertBillWatchAsync(
                    AutoRunLimits.SourceApi,
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
                AutoRunLog.Progress(
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
                    new AutoRunBillInput(
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

            AutoRunLog.Progress(observer, pageEnd, $"{phase}第 {page}/{totalPages} 页处理完成");
            if (list.Items.Count == 0 || page * AutoRunLimits.PageSize >= list.Total)
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

        for (var attempt = 1; attempt <= AutoRunLimits.RateLimitAttempts; attempt++)
        {
            using var timeout = AutoRunLog.CreateTimeout(options.TimeoutSeconds);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var timer = Stopwatch.StartNew();
            result = await _api.GetYljgListUpoutAsync(options, request, linked.Token).ConfigureAwait(false);
            apiMilliseconds += timer.ElapsedMilliseconds;

            if (result.Call.Ok || !IsRateLimited(result.Call) || attempt == AutoRunLimits.RateLimitAttempts)
            {
                break;
            }

            var delay = TimeSpan.FromSeconds(attempt * 2);
            AutoRunLog.Log(
                observer,
                "上游限流",
                $"接口限流，{delay.TotalSeconds:F0} 秒后重试（{attempt}/{AutoRunLimits.RateLimitAttempts - 1}）",
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

    public async Task ProcessRetriesAsync(
        MsfxApiOptions options,
        AutoRunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var retries = await _pull.GetDueBillRetriesAsync(AutoRunLimits.SourceApi, AutoRunLimits.DueRetryLimit, ct).ConfigureAwait(false);
        if (retries.Count == 0)
        {
            AutoRunLog.Progress(observer, 15, "没有待重试单据");
            return;
        }

        AutoRunLog.Log(observer, "重试", $"发现待重试单据 {retries.Count} 条，优先处理", TraceEntryState.Info);
        var retryIndex = 0;
        foreach (var retry in retries)
        {
            ct.ThrowIfCancellationRequested();
            retryIndex++;
            if (!state.ProcessedBillCodes.Add(retry.BillCode))
            {
                continue;
            }

            AutoRunLog.Progress(
                observer,
                8 + retryIndex * (7d / retries.Count),
                $"重试单据 {retryIndex}/{retries.Count}：{retry.BillCode}");

            await IngestBillAsync(
                options,
                state,
                observer,
                new AutoRunBillInput(
                    retry.BillCode,
                    retry.FromRefUserId,
                    string.Empty,
                    retry.ToRefUserId,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "{}",
                    FromRetry: true,
                    FromWatch: false,
                    WatchStatus: null),
                ct).ConfigureAwait(false);
        }

        AutoRunLog.Progress(observer, 15, $"待重试单据处理完成，共 {retries.Count} 条");
        await observer.DataChangedAsync(MsfxAutoRunData.PullAudit, ct).ConfigureAwait(false);
    }

    public async Task ProcessWatchesAsync(
        MsfxApiOptions options,
        AutoRunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var watches = await _pull.GetDueBillWatchesAsync(AutoRunLimits.SourceApi, AutoRunLimits.DueWatchLimit, ct).ConfigureAwait(false);
        if (watches.Count == 0)
        {
            AutoRunLog.Progress(observer, 85, "没有待确认补偿单据");
            return;
        }

        AutoRunLog.Log(observer, "待确认补偿", $"发现待确认单据 {watches.Count} 条，开始补偿重查", TraceEntryState.Info);
        var watchIndex = 0;
        foreach (var watch in watches)
        {
            ct.ThrowIfCancellationRequested();
            watchIndex++;
            if (!state.ProcessedBillCodes.Add(watch.BillCode))
            {
                continue;
            }

            AutoRunLog.Progress(
                observer,
                75 + watchIndex * (10d / watches.Count),
                $"补偿重查 {watchIndex}/{watches.Count}：{watch.BillCode}");
            await IngestBillAsync(
                options,
                state,
                observer,
                new AutoRunBillInput(
                    watch.BillCode,
                    watch.FromRefUserId,
                    watch.FromEntName,
                    watch.ToRefUserId,
                    watch.BillType,
                    watch.BillTime,
                    watch.BillUploadTime,
                    watch.RawJson ?? "{}",
                    FromRetry: false,
                    FromWatch: true,
                    watch.LastSeenStatus),
                ct).ConfigureAwait(false);
        }

        AutoRunLog.Progress(observer, 85, $"待确认补偿完成，共 {watches.Count} 条");
        await observer.DataChangedAsync(MsfxAutoRunData.PullAudit, ct).ConfigureAwait(false);
    }

    private async Task IngestBillAsync(
        MsfxApiOptions options,
        AutoRunState state,
        IMsfxAutoRunObserver observer,
        AutoRunBillInput bill,
        CancellationToken ct)
    {
        var toRef = string.IsNullOrWhiteSpace(bill.ToRefUserId) ? options.RefEntId : bill.ToRefUserId;
        var fromRef = AutoRunLog.Normalize(bill.FromRefUserId);

        var billId = await _ingest.UpsertInboundBillAsync(
            state.BatchId,
            bill.BillCode,
            bill.BillType ?? string.Empty,
            bill.BillTime ?? string.Empty,
            bill.BillUploadTime ?? string.Empty,
            bill.FromRefUserId ?? string.Empty,
            bill.FromEntName ?? string.Empty,
            toRef ?? string.Empty,
            "2",
            bill.RawJson,
            ct).ConfigureAwait(false);

        MsfxListUpoutDetailResult detail;
        try
        {
            using var timeout = AutoRunLog.CreateTimeout(options.TimeoutSeconds);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var timer = Stopwatch.StartNew();
            detail = await _api.GetYljgListUpoutDetailAsync(
                options,
                new MsfxListUpoutDetailRequest(options.RefEntId, bill.BillCode, toRef, fromRef),
                linked.Token).ConfigureAwait(false);
            state.DetailApiMs += timer.ElapsedMilliseconds;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await QueueRetryAsync(
                state,
                observer,
                bill,
                fromRef,
                toRef,
                $"详情 API 请求超时（{Math.Clamp(options.TimeoutSeconds, 3, 120)} 秒）",
                ct).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await QueueRetryAsync(state, observer, bill, fromRef, toRef, ex.Message, ct).ConfigureAwait(false);
            return;
        }

        if (!detail.Call.Ok)
        {
            await QueueRetryAsync(
                state,
                observer,
                bill,
                fromRef,
                toRef,
                AutoRunLog.BuildApiErrorMessage(detail.Call),
                ct).ConfigureAwait(false);
            return;
        }

        var timerIngest = Stopwatch.StartNew();
        var ingest = await _ingest.IngestUpoutDetailAsync(
            billId,
            bill.BillCode,
            detail.DrugItems
                .Select(drug => (
                    drug.PhysicName,
                    drug.PackageSpec,
                    drug.PrepnSpec,
                    drug.ProduceBatchNo,
                    (IReadOnlyList<(
                        string Code,
                        string CodeLevel,
                        string? Level1Code,
                        string? Level2Code,
                        string? Level3Code,
                        string? Level4Code,
                        string? Level5Code)>)drug.TraceCodes
                        .Select(code => (
                            code.Code,
                            code.CodeLevel,
                            code.Level1Code,
                            code.Level2Code,
                            code.Level3Code,
                            code.Level4Code,
                            code.Level5Code))
                        .ToList()))
                .ToList(),
            ct).ConfigureAwait(false);
        state.IngestMs += timerIngest.ElapsedMilliseconds;
        state.SucceedCount++;
        state.Codes += ingest.ProcessedCodes;
        var ingestSummary =
            $"药品处理 {ingest.ProcessedItems}/新增 {ingest.NewItems}，" +
            $"码处理 {ingest.ProcessedCodes}/新增 {ingest.NewCodes}";

        if (bill.FromRetry)
        {
            state.RetrySucceeded++;
            await _pull.MarkBillRetrySucceededAsync(AutoRunLimits.SourceApi, bill.BillCode, ct).ConfigureAwait(false);
            AutoRunLog.Log(observer, "重试", $"单据 {bill.BillCode} 重试成功：{ingestSummary}", TraceEntryState.Success);
        }
        else if (bill.FromWatch)
        {
            state.WatchResolved++;
            AutoRunLog.Log(observer, "待确认补偿", $"单据 {bill.BillCode} 已转入库：{ingestSummary}", TraceEntryState.Success);
        }
        else
        {
            var logState = ingest.NewCodes > 0 ? TraceEntryState.Success : TraceEntryState.Info;
            AutoRunLog.Log(observer, "落库", $"单据 {bill.BillCode}：{ingestSummary}", logState);
        }

        await _pull.MarkBillWatchResolvedAsync(AutoRunLimits.SourceApi, bill.BillCode, ct).ConfigureAwait(false);
    }

    private async Task QueueRetryAsync(
        AutoRunState state,
        IMsfxAutoRunObserver observer,
        AutoRunBillInput bill,
        string? fromRef,
        string? toRef,
        string error,
        CancellationToken ct)
    {
        if (bill.FromWatch)
        {
            await _pull.RescheduleBillWatchAsync(
                AutoRunLimits.SourceApi,
                bill.BillCode,
                bill.WatchStatus,
                error,
                ct).ConfigureAwait(false);
            state.WatchDeferred++;
            AutoRunLog.Log(observer, "待确认补偿", $"单据 {bill.BillCode} 暂未就绪，已延后重查：{error}", TraceEntryState.Warning);
            return;
        }

        await _pull.UpsertBillRetryAsync(
            AutoRunLimits.SourceApi,
            bill.BillCode,
            fromRef,
            toRef,
            error,
            ct).ConfigureAwait(false);
        state.RetryQueued++;
        if (bill.FromRetry)
        {
            state.RetryFailed++;
            AutoRunLog.Log(observer, "重试", $"单据 {bill.BillCode} 仍失败：{error}", TraceEntryState.Warning);
        }
        else
        {
            AutoRunLog.Log(observer, "子码解析", $"单据 {bill.BillCode} 失败，已入重试队列：{error}", TraceEntryState.Warning);
        }
    }
}
