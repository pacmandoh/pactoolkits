using System.Diagnostics;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

/// <summary>
/// 执行 MSFX 单据拉取、入库、映射和任务创建，并支持恢复中断批次
/// </summary>
public sealed partial class MsfxAutoRunService : IMsfxAutoRunService
{
    private readonly IMsfxApiClient _api;
    private readonly IMsfxAutoRunStore _store;

    private const string SourceApi = "listupout";
    private const string InterruptedBatchError = "应用异常退出，批次未完成";
    private const int PageSize = 50;
    private const int MappingBatchSize = 1000;
    private const int MappingMaxRows = 50000;
    // 同窗补扫上限；仍漏的靠下轮回看窗口补偿
    private const int ReconciliationPasses = 3;
    private const int RateLimitAttempts = 3;

    public MsfxAutoRunService(IMsfxApiClient api, IMsfxAutoRunStore store)
    {
        _api = api;
        _store = store;
    }

    public async Task<int> RecoverInterruptedAsync(CancellationToken ct)
    {
        await using var runLock = await _store.TryAcquireRunLockAsync(SourceApi, ct).ConfigureAwait(false);
        return runLock is null
            ? 0
            : await _store.FailInterruptedPullBatchesAsync(SourceApi, InterruptedBatchError, ct).ConfigureAwait(false);
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

        // 进度保持单调递增，避免补扫或重试造成界面进度回退
        observer = new MonotonicObserver(observer);
        // 同一来源接口仅允许一个任务运行，避免重复生成批次
        await using var runLock =
            await _store.TryAcquireRunLockAsync(SourceApi, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("另一个 MSFX 自动巡检正在运行，请等待其完成");

        await _store.FailInterruptedPullBatchesAsync(SourceApi, InterruptedBatchError, ct).ConfigureAwait(false);

        var state = new RunState();
        var total = Stopwatch.StartNew();
        try
        {
            Progress(observer, 2, "准备巡检");
            state.Window = await _store.GetPullWindowAsync(SourceApi, ct).ConfigureAwait(false);
            Log(observer, "任务", $"开始执行自动化拉取（{state.Window.BeginAt:yyyy-MM-dd HH:mm:ss} ~ {state.Window.EndAt:yyyy-MM-dd HH:mm:ss}）", TraceEntryState.Info);
            Progress(observer, 5, $"拉取窗口 {state.Window.BeginAt:MM-dd HH:mm} ~ {state.Window.EndAt:MM-dd HH:mm}");

            var batch = await _store.StartPullBatchAsync(
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
            await _store.FinishPullBatchAsync(
                state.BatchId,
                batchStatus,
                state.SucceedCount,
                state.FailCount,
                null,
                CancellationToken.None).ConfigureAwait(false);
            await _store.AdvancePullCursorAsync(
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
}
