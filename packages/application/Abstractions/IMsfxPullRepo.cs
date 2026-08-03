using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 拉取游标、批次、重试与观察队列
/// </summary>
public interface IMsfxPullRepo
{
    Task<IAsyncDisposable?> TryAcquireRunLockAsync(string sourceApi, CancellationToken ct);

    Task<int> FailInterruptedPullBatchesAsync(string sourceApi, string error, CancellationToken ct);

    Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct);

    Task<MsfxPullCursorState> GetPullCursorAsync(string sourceApi, CancellationToken ct);

    Task<bool> AdvancePullCursorToAsync(string sourceApi, DateTimeOffset target, CancellationToken ct);

    Task<MsfxPullBatchStartResult> StartPullBatchAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        CancellationToken ct);

    Task FinishPullBatchAsync(
        long batchId,
        string status,
        int successCount,
        int failCount,
        string? errMsg,
        CancellationToken ct);

    Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct);

    Task AdvancePullCursorAsync(
        string sourceApi,
        DateTimeOffset beginAt,
        DateTimeOffset endAt,
        long batchId,
        string batchStatus,
        CancellationToken ct);

    Task<IReadOnlyList<MsfxBillRetryRow>> GetDueBillRetriesAsync(
        string sourceApi,
        int limit,
        CancellationToken ct);

    Task UpsertBillRetryAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? lastError,
        CancellationToken ct);

    Task MarkBillRetrySucceededAsync(string sourceApi, string billCode, CancellationToken ct);

    Task UpsertBillWatchAsync(
        string sourceApi,
        string billCode,
        string? fromRefUserId,
        string? toRefUserId,
        string? fromEntName,
        string? billType,
        string? billTime,
        string? billUploadTime,
        string? lastSeenStatus,
        string? rawJson,
        CancellationToken ct);

    Task<IReadOnlyList<MsfxBillWatchRow>> GetDueBillWatchesAsync(
        string sourceApi,
        int limit,
        CancellationToken ct);

    Task MarkBillWatchResolvedAsync(string sourceApi, string billCode, CancellationToken ct);

    Task RescheduleBillWatchAsync(
        string sourceApi,
        string billCode,
        string? lastSeenStatus,
        string? lastError,
        CancellationToken ct);
}
