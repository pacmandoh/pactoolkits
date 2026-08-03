using System.Diagnostics;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

/// <summary>
/// MSFX 自动巡检编排：串接 pull 与 map/build
/// </summary>
public sealed class MsfxAutoRunService : IMsfxAutoRunService
{
    private readonly IMsfxPullRepo _pull;
    private readonly AutoRunPull _pullFlow;
    private readonly AutoRunMap _mapFlow;

    public MsfxAutoRunService(
        IMsfxApiClient api,
        IMsfxPullRepo pull,
        IMsfxIngestRepo ingest,
        IMsfxMappingRepo mapping,
        IMsfxInjectRepo inject)
    {
        _pull = pull ?? throw new ArgumentNullException(nameof(pull));
        _pullFlow = new AutoRunPull(
            api ?? throw new ArgumentNullException(nameof(api)),
            pull,
            ingest ?? throw new ArgumentNullException(nameof(ingest)));
        _mapFlow = new AutoRunMap(
            mapping ?? throw new ArgumentNullException(nameof(mapping)),
            inject ?? throw new ArgumentNullException(nameof(inject)));
    }

    public async Task<int> RecoverInterruptedAsync(CancellationToken ct)
    {
        await using var runLock = await _pull.TryAcquireRunLockAsync(AutoRunLimits.SourceApi, ct).ConfigureAwait(false);
        return runLock is null
            ? 0
            : await _pull.FailInterruptedPullBatchesAsync(
                AutoRunLimits.SourceApi,
                AutoRunLimits.InterruptedBatchError,
                ct).ConfigureAwait(false);
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

        observer = AutoRunLog.WrapMonotonic(observer);
        await using var runLock =
            await _pull.TryAcquireRunLockAsync(AutoRunLimits.SourceApi, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("另一个 MSFX 自动巡检正在运行，请等待其完成");

        await _pull.FailInterruptedPullBatchesAsync(
            AutoRunLimits.SourceApi,
            AutoRunLimits.InterruptedBatchError,
            ct).ConfigureAwait(false);

        var state = new AutoRunState();
        var total = Stopwatch.StartNew();
        try
        {
            AutoRunLog.Progress(observer, 2, "准备巡检");
            state.Window = await _pull.GetPullWindowAsync(AutoRunLimits.SourceApi, ct).ConfigureAwait(false);
            AutoRunLog.Log(
                observer,
                "任务",
                $"开始执行自动化拉取（{state.Window.BeginAt:yyyy-MM-dd HH:mm:ss} ~ {state.Window.EndAt:yyyy-MM-dd HH:mm:ss}）",
                TraceEntryState.Info);
            AutoRunLog.Progress(
                observer,
                5,
                $"拉取窗口 {state.Window.BeginAt:MM-dd HH:mm} ~ {state.Window.EndAt:MM-dd HH:mm}");

            var batch = await _pull.StartPullBatchAsync(
                AutoRunLimits.SourceApi,
                state.Window.BeginAt,
                state.Window.EndAt,
                ct).ConfigureAwait(false);
            state.BatchId = batch.BatchId;
            AutoRunLog.Log(observer, "批次", $"拉取批次已创建：#{state.BatchId}", TraceEntryState.Success);
            AutoRunLog.Progress(observer, 8, $"批次 #{state.BatchId} 已创建");
            await observer.DataChangedAsync(MsfxAutoRunData.PullAudit, ct).ConfigureAwait(false);

            await _pullFlow.ProcessRetriesAsync(options, state, observer, ct).ConfigureAwait(false);
            await _pullFlow.PullPagesAsync(options, state, observer, ct).ConfigureAwait(false);
            await _pullFlow.ProcessWatchesAsync(options, state, observer, ct).ConfigureAwait(false);

            var batchStatus = state.FailCount > 0 ? "FAILED" : "SUCCESS";
            await _pull.FinishPullBatchAsync(
                state.BatchId,
                batchStatus,
                state.SucceedCount,
                state.FailCount,
                null,
                CancellationToken.None).ConfigureAwait(false);
            await _pull.AdvancePullCursorAsync(
                AutoRunLimits.SourceApi,
                state.Window.BeginAt,
                state.Window.EndAt,
                state.BatchId,
                batchStatus,
                CancellationToken.None).ConfigureAwait(false);
            state.BatchFinalized = true;
            await observer.DataChangedAsync(MsfxAutoRunData.PullAudit, ct).ConfigureAwait(false);

            await _mapFlow.MapAndBuildAsync(state, observer, ct).ConfigureAwait(false);

            AutoRunLog.Progress(observer, 100, "巡检完成");
            AutoRunLog.Log(
                observer,
                "性能",
                $"总耗时 {AutoRunLog.FormatElapsed(total.ElapsedMilliseconds)}，列表API {AutoRunLog.FormatElapsed(state.ListApiMs)}，" +
                $"详情API {AutoRunLog.FormatElapsed(state.DetailApiMs)}，入库 {AutoRunLog.FormatElapsed(state.IngestMs)}，" +
                $"映射 {AutoRunLog.FormatElapsed(state.MapMs)}，建任务 {AutoRunLog.FormatElapsed(state.TaskBuildMs)}",
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

    private async Task FinalizeFailedBatchAsync(AutoRunState state, IMsfxAutoRunObserver observer)
    {
        try
        {
            await _pull.FinishPullBatchAsync(
                state.BatchId,
                "FAILED",
                state.SucceedCount,
                Math.Max(state.FailCount, 1),
                string.IsNullOrWhiteSpace(state.Error) ? "执行失败，详见运行日志" : state.Error,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AutoRunLog.Log(observer, "批次结算", $"批次#{state.BatchId} 状态回写失败：{ex.Message}", TraceEntryState.Failed);
        }
    }
}
