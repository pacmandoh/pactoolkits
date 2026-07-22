using System.Diagnostics;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

public sealed partial class MsfxAutoRunService
{
    private async Task ProcessRetriesAsync(
        MsfxApiOptions options,
        RunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var retries = await _store.GetDueBillRetriesAsync(SourceApi, 200, ct).ConfigureAwait(false);
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

    private async Task ProcessWatchesAsync(
        MsfxApiOptions options,
        RunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var watches = await _store.GetDueBillWatchesAsync(SourceApi, 200, ct).ConfigureAwait(false);
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

        var billId = await _store.UpsertInboundBillAsync(
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
                BuildApiErrorMessage(detail.Call),
                ct).ConfigureAwait(false);
            return;
        }

        var timerIngest = Stopwatch.StartNew();
        var ingest = await _store.IngestUpoutDetailAsync(
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
            await _store.MarkBillRetrySucceededAsync(SourceApi, bill.BillCode, ct).ConfigureAwait(false);
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

        await _store.MarkBillWatchResolvedAsync(SourceApi, bill.BillCode, ct).ConfigureAwait(false);
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
            await _store.RescheduleBillWatchAsync(
                SourceApi,
                bill.BillCode,
                bill.WatchStatus,
                error,
                ct).ConfigureAwait(false);
            state.WatchDeferred++;
            Log(observer, "待确认补偿", $"单据 {bill.BillCode} 暂未就绪，已延后重查：{error}", TraceEntryState.Warning);
            return;
        }

        await _store.UpsertBillRetryAsync(
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

}
