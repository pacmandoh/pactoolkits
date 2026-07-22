using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

public sealed partial class MsfxAutoRunService
{
    private async Task FinalizeFailedBatchAsync(RunState state, IMsfxAutoRunObserver observer)
    {
        try
        {
            await _store.FinishPullBatchAsync(
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

    private static void Log(
        IMsfxAutoRunObserver observer,
        string stage,
        string message,
        TraceEntryState state)
        => observer.Report(new MsfxAutoRunUpdate(Stage: stage, Message: message, State: state));

    /// <summary>包装观察者：Progress 单调不减，避免 UI 回跳</summary>
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
