using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

public sealed class MsfxAutoRunService(
    IMsfxApiClient api,
    IMsfxAutoRunStore store) : IMsfxAutoRunService
{
    private const string SourceApi = "listupout";
    private const string InterruptedBatchError = "应用异常退出，批次未完成";
    private const int PageSize = 50;
    private const int MappingBatchSize = 1000;
    private const int MappingMaxRows = 50000;
    private const int ReconciliationPasses = 3;
    private const int RateLimitAttempts = 3;

    public async Task<int> RecoverInterruptedAsync(CancellationToken ct)
    {
        await using var runLock = await store.TryAcquireRunLockAsync(SourceApi, ct).ConfigureAwait(false);
        return runLock is null
            ? 0
            : await store.FailInterruptedPullBatchesAsync(SourceApi, InterruptedBatchError, ct).ConfigureAwait(false);
    }

    public async Task<MsfxAutoRunResult> RunAsync(
        MsfxAutoRunRequest request,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observer);

        var options = request.Api ?? throw new ArgumentException("MSFX API options are required.", nameof(request));
        if (string.IsNullOrWhiteSpace(options.RefEntId))
        {
            throw new InvalidOperationException("请先在设置页面配置接收企业 RefEntId");
        }

        observer = new MonotonicObserver(observer);
        await using var runLock =
            await store.TryAcquireRunLockAsync(SourceApi, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("另一个 MSFX 自动巡检正在运行，请等待其完成");

        await store.FailInterruptedPullBatchesAsync(SourceApi, InterruptedBatchError, ct).ConfigureAwait(false);

        var state = new RunState();
        var total = Stopwatch.StartNew();
        try
        {
            Progress(observer, 2, "准备巡检");
            state.Window = await store.GetPullWindowAsync(SourceApi, ct).ConfigureAwait(false);
            Log(observer, "任务", $"开始执行自动化拉取（{state.Window.BeginAt:yyyy-MM-dd HH:mm:ss} ~ {state.Window.EndAt:yyyy-MM-dd HH:mm:ss}）", TraceEntryState.Info);
            Progress(observer, 5, $"拉取窗口 {state.Window.BeginAt:MM-dd HH:mm} ~ {state.Window.EndAt:MM-dd HH:mm}");

            var batch = await store.StartPullBatchAsync(
                SourceApi,
                state.Window.BeginAt,
                state.Window.EndAt,
                ct).ConfigureAwait(false);
            state.BatchId = batch.BatchId;
            Log(observer, "批次", $"拉取批次已创建：#{state.BatchId}", TraceEntryState.Success);
            Progress(observer, 8, $"批次 #{state.BatchId} 已创建");
            await observer.DataChangedAsync(MsfxAutoRunData.PullAudit, ct).ConfigureAwait(false);

            await ProcessRetriesAsync(options, state, observer, ct).ConfigureAwait(false);
            await PullPagesAsync(options, state, observer, ct).ConfigureAwait(false);
            await ProcessWatchesAsync(options, state, observer, ct).ConfigureAwait(false);

            var batchStatus = state.FailCount > 0 ? "FAILED" : "SUCCESS";
            await store.FinishPullBatchAsync(
                state.BatchId,
                batchStatus,
                state.SucceedCount,
                state.FailCount,
                null,
                CancellationToken.None).ConfigureAwait(false);
            await store.AdvancePullCursorAsync(
                SourceApi,
                state.Window.BeginAt,
                state.Window.EndAt,
                state.BatchId,
                batchStatus,
                CancellationToken.None).ConfigureAwait(false);
            state.BatchFinalized = true;
            await observer.DataChangedAsync(MsfxAutoRunData.PullAudit, ct).ConfigureAwait(false);

            await MapAndBuildAsync(state, observer, ct).ConfigureAwait(false);

            Progress(observer, 100, "巡检完成");
            Log(
                observer,
                "性能",
                $"总耗时 {FormatElapsed(total.ElapsedMilliseconds)}，列表API {FormatElapsed(state.ListApiMs)}，" +
                $"详情API {FormatElapsed(state.DetailApiMs)}，入库 {FormatElapsed(state.IngestMs)}，" +
                $"映射 {FormatElapsed(state.MapMs)}，建任务 {FormatElapsed(state.TaskBuildMs)}",
                TraceEntryState.Info);

            return state.ToResult(total.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            state.Error = ex.Message;
            throw;
        }
        finally
        {
            if (state.BatchId > 0 && !state.BatchFinalized)
            {
                await FinalizeFailedBatchAsync(state, observer).ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessRetriesAsync(
        MsfxApiOptions options,
        RunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var retries = await store.GetDueBillRetriesAsync(SourceApi, 200, ct).ConfigureAwait(false);
        if (retries.Count == 0)
        {
            Progress(observer, 15, "没有待重试单据");
            return;
        }

        Log(observer, "重试", $"发现待重试单据 {retries.Count} 条，优先处理", TraceEntryState.Info);
        var retryIndex = 0;
        foreach (var retry in retries)
        {
            ct.ThrowIfCancellationRequested();
            retryIndex++;
            if (!state.ProcessedBillCodes.Add(retry.BillCode))
            {
                continue;
            }

            Progress(
                observer,
                8 + retryIndex * (7d / retries.Count),
                $"重试单据 {retryIndex}/{retries.Count}：{retry.BillCode}");

            await IngestBillAsync(
                options,
                state,
                observer,
                new BillInput(
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

        Progress(observer, 15, $"待重试单据处理完成，共 {retries.Count} 条");
        await observer.DataChangedAsync(MsfxAutoRunData.PullAudit, ct).ConfigureAwait(false);
    }

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
                await store.UpdatePullBatchRequestIdAsync(
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
                await store.UpsertBillWatchAsync(
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

    private async Task ProcessWatchesAsync(
        MsfxApiOptions options,
        RunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var watches = await store.GetDueBillWatchesAsync(SourceApi, 200, ct).ConfigureAwait(false);
        if (watches.Count == 0)
        {
            Progress(observer, 85, "没有待确认补偿单据");
            return;
        }

        Log(observer, "待确认补偿", $"发现待确认单据 {watches.Count} 条，开始补偿重查", TraceEntryState.Info);
        var watchIndex = 0;
        foreach (var watch in watches)
        {
            ct.ThrowIfCancellationRequested();
            watchIndex++;
            if (!state.ProcessedBillCodes.Add(watch.BillCode))
            {
                continue;
            }

            Progress(
                observer,
                75 + watchIndex * (10d / watches.Count),
                $"补偿重查 {watchIndex}/{watches.Count}：{watch.BillCode}");
            await IngestBillAsync(
                options,
                state,
                observer,
                new BillInput(
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

        Progress(observer, 85, $"待确认补偿完成，共 {watches.Count} 条");
        await observer.DataChangedAsync(MsfxAutoRunData.PullAudit, ct).ConfigureAwait(false);
    }

    private async Task IngestBillAsync(
        MsfxApiOptions options,
        RunState state,
        IMsfxAutoRunObserver observer,
        BillInput bill,
        CancellationToken ct)
    {
        var toRef = string.IsNullOrWhiteSpace(bill.ToRefUserId) ? options.RefEntId : bill.ToRefUserId;
        var fromRef = Normalize(bill.FromRefUserId);

        var billId = await store.UpsertInboundBillAsync(
            state.BatchId,
            bill.BillCode,
            bill.BillType ?? string.Empty,
            bill.BillTime ?? string.Empty,
            bill.BillUploadTime ?? string.Empty,
            bill.FromRefUserId ?? string.Empty,
            bill.FromEntName ?? string.Empty,
            toRef ?? string.Empty,
            string.Empty,
            string.Empty,
            "2",
            bill.RawJson,
            ct).ConfigureAwait(false);

        MsfxListUpoutDetailResult detail;
        try
        {
            using var timeout = CreateTimeout(options.TimeoutSeconds);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var timer = Stopwatch.StartNew();
            detail = await api.GetYljgListUpoutDetailAsync(
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
                BuildApiErrorMessage(detail.Call),
                ct).ConfigureAwait(false);
            return;
        }

        var timerIngest = Stopwatch.StartNew();
        var ingest = await store.IngestUpoutDetailAsync(
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
            await store.MarkBillRetrySucceededAsync(SourceApi, bill.BillCode, ct).ConfigureAwait(false);
            Log(observer, "重试", $"单据 {bill.BillCode} 重试成功：{ingestSummary}", TraceEntryState.Success);
        }
        else if (bill.FromWatch)
        {
            state.WatchResolved++;
            Log(observer, "待确认补偿", $"单据 {bill.BillCode} 已转入库：{ingestSummary}", TraceEntryState.Success);
        }
        else
        {
            var logState = ingest.NewCodes > 0 ? TraceEntryState.Success : TraceEntryState.Info;
            Log(observer, "落库", $"单据 {bill.BillCode}：{ingestSummary}", logState);
        }

        await store.MarkBillWatchResolvedAsync(SourceApi, bill.BillCode, ct).ConfigureAwait(false);
    }

    private async Task QueueRetryAsync(
        RunState state,
        IMsfxAutoRunObserver observer,
        BillInput bill,
        string? fromRef,
        string? toRef,
        string error,
        CancellationToken ct)
    {
        if (bill.FromWatch)
        {
            await store.RescheduleBillWatchAsync(
                SourceApi,
                bill.BillCode,
                bill.WatchStatus,
                error,
                ct).ConfigureAwait(false);
            state.WatchDeferred++;
            Log(observer, "待确认补偿", $"单据 {bill.BillCode} 暂未就绪，已延后重查：{error}", TraceEntryState.Warning);
            return;
        }

        await store.UpsertBillRetryAsync(
            SourceApi,
            bill.BillCode,
            fromRef,
            toRef,
            error,
            ct).ConfigureAwait(false);
        state.RetryQueued++;
        if (bill.FromRetry)
        {
            state.RetryFailed++;
            Log(observer, "重试", $"单据 {bill.BillCode} 仍失败：{error}", TraceEntryState.Warning);
        }
        else
        {
            Log(observer, "子码解析", $"单据 {bill.BillCode} 失败，已入重试队列：{error}", TraceEntryState.Warning);
        }
    }

    private async Task MapAndBuildAsync(
        RunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var mapTimer = Stopwatch.StartNew();
        var before = await store.GetMappingStatusSnapshotAsync(ct).ConfigureAwait(false);
        Log(observer, "映射自检", MappingStatus("执行前", before), TraceEntryState.Info);

        if (observer.IsManualWriteActive)
        {
            Log(observer, "自动巡检", "手动敏感操作进行中，跳过映射与建任务", TraceEntryState.Info);
            Progress(observer, 98, "跳过映射与建任务");
            return;
        }

        MsfxMapApplyResult map;
        MsfxMappingStatusSnapshot after;
        if (before.PendingCount == 0)
        {
            map = new MsfxMapApplyResult(0, 0, 0);
            after = before;
            Progress(observer, 96, "无待映射数据");
            Log(observer, "映射", "无待映射数据，跳过自动映射", TraceEntryState.Info);
        }
        else
        {
            Progress(observer, 88, "准备自动映射");
            map = await ApplyMappingInBatchesAsync(observer, ct).ConfigureAwait(false);
            after = await store.GetMappingStatusSnapshotAsync(ct).ConfigureAwait(false);
            Progress(observer, 96, "自动映射完成");
            var mapState = map.ReviewCount > 0
                ? TraceEntryState.Warning
                : TraceEntryState.Success;
            Log(observer, "映射", $"处理 {map.ProcessedCount}，命中 {map.MappedCount}，待人工 {map.ReviewCount}", mapState);
            await observer.DataChangedAsync(MsfxAutoRunData.MappingQueue, ct).ConfigureAwait(false);
        }

        state.MapMs += mapTimer.ElapsedMilliseconds;
        state.MapProcessed = map.ProcessedCount;
        state.MapMatched = map.MappedCount;
        state.MapReview = map.ReviewCount;
        Log(
            observer,
            "映射自检",
            MappingStatus("执行后", after),
            before.PendingCount == 0 ? TraceEntryState.Info : TraceEntryState.Success);

        var taskTimer = Stopwatch.StartNew();
        var tasks = await store.BuildInjectsAsync(500, ct).ConfigureAwait(false);
        state.TaskBuildMs += taskTimer.ElapsedMilliseconds;
        state.CreatedTasks = tasks.CreatedTasks;
        state.TaskedCodes = tasks.TaskedCodes;
        Progress(observer, 98, "构建注入任务");
        Log(
            observer,
            "建任务",
            $"创建任务 {tasks.CreatedTasks}，下发码 {tasks.TaskedCodes}",
            tasks.CreatedTasks > 0 ? TraceEntryState.Success : TraceEntryState.Warning);
        await observer.DataChangedAsync(MsfxAutoRunData.TaskQueue, ct).ConfigureAwait(false);
    }

    private async Task<MsfxMapApplyResult> ApplyMappingInBatchesAsync(
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var processed = 0;
        var mapped = 0;
        var review = 0;

        while (processed < MappingMaxRows)
        {
            var limit = Math.Min(MappingBatchSize, MappingMaxRows - processed);
            var batch = await store.ApplyMappingAsync(limit, ct).ConfigureAwait(false);
            processed += batch.ProcessedCount;
            mapped += batch.MappedCount;
            review += batch.ReviewCount;

            if (batch.ProcessedCount < limit)
            {
                break;
            }

            Progress(
                observer,
                88 + Math.Min(7, processed * (7d / MappingMaxRows)),
                $"执行自动映射 {processed}/{MappingMaxRows}");
        }

        return new MsfxMapApplyResult(processed, mapped, review);
    }

    private async Task FinalizeFailedBatchAsync(RunState state, IMsfxAutoRunObserver observer)
    {
        try
        {
            await store.FinishPullBatchAsync(
                state.BatchId,
                "FAILED",
                state.SucceedCount,
                Math.Max(state.FailCount, 1),
                string.IsNullOrWhiteSpace(state.Error) ? "执行失败，详见运行日志" : state.Error,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log(observer, "批次结算", $"批次#{state.BatchId} 状态回写失败：{ex.Message}", TraceEntryState.Failed);
        }
    }

    private static void Progress(IMsfxAutoRunObserver observer, double value, string status)
        => observer.Report(new MsfxAutoRunUpdate(Math.Clamp(value, 0, 100), status));

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
            result = await api.GetYljgListUpoutAsync(options, request, linked.Token).ConfigureAwait(false);
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

    private static bool IsRateLimited(MsfxApiCallResult call)
        => string.Equals(call.BizCode?.Trim(), "7", StringComparison.OrdinalIgnoreCase)
           || call.BizMessage?.Contains("App Call Limited", StringComparison.OrdinalIgnoreCase) == true
           || call.Summary?.Contains("App Call Limited", StringComparison.OrdinalIgnoreCase) == true;

    private static void Log(
        IMsfxAutoRunObserver observer,
        string stage,
        string message,
        TraceEntryState state)
        => observer.Report(new MsfxAutoRunUpdate(Stage: stage, Message: message, State: state));

    private sealed class MonotonicObserver(IMsfxAutoRunObserver inner) : IMsfxAutoRunObserver
    {
        private double _progress;

        public bool IsManualWriteActive => inner.IsManualWriteActive;

        public void Report(MsfxAutoRunUpdate update)
        {
            if (update.Progress is { } value)
            {
                _progress = Math.Max(_progress, Math.Clamp(value, 0, 100));
                update = update with { Progress = _progress };
            }

            inner.Report(update);
        }

        public Task DataChangedAsync(MsfxAutoRunData data, CancellationToken ct)
            => inner.DataChangedAsync(data, ct);

    }

    private static CancellationTokenSource CreateTimeout(int seconds)
        => new(TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 120)));

    private static string? Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string FormatElapsed(long milliseconds) => $"{milliseconds / 1000d:F2}s";

    private static string MappingStatus(string prefix, MsfxMappingStatusSnapshot status)
        => $"{prefix} PENDING {status.PendingCount}，MAPPED {status.MappedCount}，" +
           $"NEED_REVIEW {status.NeedReviewCount}，FAILED {status.FailedCount}，TOTAL {status.TotalCount}";

    private static string BuildApiErrorMessage(MsfxApiCallResult call)
    {
        var parts = new List<string>(4);
        var business = $"{call.BizCode} {call.BizMessage}".Trim();
        if (!string.IsNullOrWhiteSpace(business))
        {
            parts.Add(business);
        }

        if (!string.IsNullOrWhiteSpace(call.Summary))
        {
            parts.Add(call.Summary.Trim());
        }

        if (!string.IsNullOrWhiteSpace(call.RequestId))
        {
            parts.Add($"request_id={call.RequestId.Trim()}");
        }

        if (parts.Count == 0 && !string.IsNullOrWhiteSpace(call.ResponseText))
        {
            var text = call.ResponseText.Trim().Replace("\r", " ").Replace("\n", " ");
            parts.Add(text.Length > 220 ? text[..220] : text);
        }

        return parts.Count == 0 ? "未知错误" : string.Join("；", parts.Distinct(StringComparer.Ordinal));
    }

    private sealed record BillInput(
        string BillCode,
        string? FromRefUserId,
        string? FromEntName,
        string? ToRefUserId,
        string? BillType,
        string? BillTime,
        string? BillUploadTime,
        string RawJson,
        bool FromRetry,
        bool FromWatch,
        string? WatchStatus);

    private sealed class RunState
    {
        public long BatchId { get; set; }
        public bool BatchFinalized { get; set; }
        public string? Error { get; set; }
        public MsfxPullWindow Window { get; set; } = new(DateTimeOffset.MinValue, DateTimeOffset.MinValue);
        public int SucceedCount { get; set; }
        public int FailCount { get; set; }
        public int ApiRows { get; set; }
        public int InboundRows { get; set; }
        public int Bills { get; set; }
        public int Codes { get; set; }
        public int RetryQueued { get; set; }
        public int RetrySucceeded { get; set; }
        public int RetryFailed { get; set; }
        public int WatchQueued { get; set; }
        public int WatchResolved { get; set; }
        public int WatchDeferred { get; set; }
        public int MapProcessed { get; set; }
        public int MapMatched { get; set; }
        public int MapReview { get; set; }
        public int CreatedTasks { get; set; }
        public int TaskedCodes { get; set; }
        public long ListApiMs { get; set; }
        public long DetailApiMs { get; set; }
        public long IngestMs { get; set; }
        public long MapMs { get; set; }
        public long TaskBuildMs { get; set; }
        public int InboundVisitedBills { get; set; }
        public HashSet<string> ListBillCodes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> InboundBillCodes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> WatchBillCodes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ProcessedBillCodes { get; } = new(StringComparer.Ordinal);

        public MsfxAutoRunResult ToResult(long elapsedMs)
            => new(
                BatchId,
                ApiRows,
                InboundRows,
                Bills,
                Codes,
                RetryQueued,
                RetrySucceeded,
                RetryFailed,
                WatchQueued,
                WatchResolved,
                WatchDeferred,
                MapProcessed,
                MapMatched,
                MapReview,
                CreatedTasks,
                TaskedCodes,
                elapsedMs,
                ListApiMs,
                DetailApiMs,
                IngestMs,
                MapMs,
                TaskBuildMs);
    }
}
