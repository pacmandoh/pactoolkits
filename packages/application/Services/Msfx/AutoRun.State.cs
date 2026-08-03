using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

internal static class AutoRunLimits
{
    public const string SourceApi = "listupout";
    public const string InterruptedBatchError = "应用异常退出，批次未完成";
    public const int PageSize = 50;
    public const int MappingBatchSize = 1000;
    public const int MappingMaxRows = 50000;
    public const int InjectMaxGroups = 500;
    public const int DueRetryLimit = 200;
    public const int DueWatchLimit = 200;
    public const int ReconciliationPasses = 3;
    public const int RateLimitAttempts = 3;
}

internal sealed class AutoRunState
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

internal sealed record AutoRunBillInput(
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
