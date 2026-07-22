using System.Diagnostics;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

public sealed partial class MsfxAutoRunService
{
    private async Task MapAndBuildAsync(
        RunState state,
        IMsfxAutoRunObserver observer,
        CancellationToken ct)
    {
        var mapTimer = Stopwatch.StartNew();
        var before = await _store.GetMappingStatusSnapshotAsync(ct).ConfigureAwait(false);
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
            after = await _store.GetMappingStatusSnapshotAsync(ct).ConfigureAwait(false);
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
        var tasks = await _store.BuildInjectsAsync(500, ct).ConfigureAwait(false);
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
            var batch = await _store.ApplyMappingAsync(limit, ct).ConfigureAwait(false);
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

    private static string MappingStatus(string prefix, MsfxMappingStatusSnapshot status)
        => $"{prefix} PENDING {status.PendingCount}，MAPPED {status.MappedCount}，" +
           $"NEED_REVIEW {status.NeedReviewCount}，FAILED {status.FailedCount}，TOTAL {status.TotalCount}";

}
