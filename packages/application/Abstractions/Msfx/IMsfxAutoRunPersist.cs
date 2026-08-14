using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// AutoRun 入库与跑锁：API 持有 advisory lock 会话；业务请求续期
/// </summary>
public interface IMsfxAutoRunPersist
{
    Task<MsfxRunLockResult> TryAcquireLockAsync(string sourceApi, CancellationToken ct);

    Task ReleaseLockAsync(string sourceApi, Guid lockId, CancellationToken ct);

    Task<bool> RenewLockAsync(string sourceApi, Guid lockId, CancellationToken ct);

    /// <summary>锁存在、未过期、且与 source 一致（source 空则只核 lockId）时续期并返回 true</summary>
    bool TryHoldLock(Guid lockId, string? sourceApi);

    Task<int> FailInterruptedPullBatchesAsync(string sourceApi, string error, CancellationToken ct);

    Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct);

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

    Task<long> UpsertInboundBillAsync(
        long batchId,
        string billCode,
        string billType,
        string billTime,
        string billUploadTime,
        string fromRefUserId,
        string fromEntName,
        string toRefUserId,
        string status,
        string rawJson,
        CancellationToken ct);

    Task<MsfxIngestDetailResult> IngestUpoutDetailAsync(
        long billId,
        string billCode,
        IReadOnlyList<MsfxIngestDrugDto> drugs,
        CancellationToken ct);
}
