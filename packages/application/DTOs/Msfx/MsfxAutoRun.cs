namespace PacToolkits.Application.DTOs;

public sealed record MsfxAutoRunRequest(MsfxApiOptions Api);

public enum MsfxAutoRunData
{
    PullAudit,
    MappingQueue,
    TaskQueue
}

public sealed record MsfxAutoRunUpdate(
    double? Progress = null,
    string? Status = null,
    string? Stage = null,
    string? Message = null,
    TraceEntryState? State = null);

public sealed record MsfxAutoRunResult(
    long BatchId,
    int ApiRows,
    int InboundRows,
    int Bills,
    int Codes,
    int RetryQueued,
    int RetrySucceeded,
    int RetryFailed,
    int WatchQueued,
    int WatchResolved,
    int WatchDeferred,
    int MapProcessed,
    int MapMatched,
    int MapReview,
    int CreatedTasks,
    int TaskedCodes,
    long ElapsedMs,
    long ListApiMs,
    long DetailApiMs,
    long IngestMs,
    long MapMs,
    long TaskBuildMs);
