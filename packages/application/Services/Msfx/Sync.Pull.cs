using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

public sealed partial class SyncService
{
    public Task<IAsyncDisposable?> TryAcquireRunLockAsync(
        string sourceApi,
        CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        return _repo.TryAcquireRunLockAsync(sourceApi, ct);
    }

    public Task<int> FailInterruptedPullBatchesAsync(
        string sourceApi,
        string error,
        CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return _repo.FailInterruptedPullBatchesAsync(sourceApi, error.Trim(), ct);
    }

    public Task<MsfxPullWindow> GetPullWindowAsync(string sourceApi, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        return _repo.GetPullWindowAsync(sourceApi, ct);
    }

    public Task<MsfxPullCursorState> GetPullCursorAsync(string sourceApi, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        return _repo.GetPullCursorAsync(sourceApi, ct);
    }

    public async Task<MsfxPullCursorState> AdvancePullCursorToAsync(
        string sourceApi,
        DateTimeOffset target,
        CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        if (target > DateTimeOffset.Now)
        {
            throw new ArgumentOutOfRangeException(nameof(target), "MSFX 拉取游标不能晚于当前时间");
        }

        await using var runLock =
            await _repo.TryAcquireRunLockAsync(sourceApi, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("MSFX 自动巡检正在运行，暂时不能调整拉取游标");

        var current = await _repo.GetPullCursorAsync(sourceApi, ct).ConfigureAwait(false);
        if (current.LastSuccessEnd is { } currentEnd && target <= currentEnd)
        {
            throw new InvalidOperationException("目标游标必须晚于当前游标；设置页只允许向前跳过数据");
        }

        if (!await _repo.AdvancePullCursorToAsync(sourceApi, target, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("拉取游标未更新，请刷新当前游标后重试");
        }

        return await _repo.GetPullCursorAsync(sourceApi, ct).ConfigureAwait(false);
    }

    public Task<MsfxPullBatchStartResult> StartPullBatchAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        if (endAt <= beginAt)
        {
            throw new ArgumentException("MSFX pull batch end time must be later than begin time.", nameof(endAt));
        }

        return _repo.StartPullBatchAsync(sourceApi, beginAt, endAt, ct);
    }

    public Task FinishPullBatchAsync(long batchId, string status, int successCount, int failCount, string? errMsg, CancellationToken ct)
    {
        RequirePositiveId(batchId, nameof(batchId));
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("MSFX pull batch status is required.", nameof(status));
        }

        if (successCount < 0 || failCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(successCount), "MSFX pull batch counts cannot be negative.");
        }

        return _repo.FinishPullBatchAsync(batchId, status.Trim(), successCount, failCount, errMsg, ct);
    }

    public Task UpdatePullBatchRequestIdAsync(long batchId, string? requestId, CancellationToken ct)
    {
        RequirePositiveId(batchId, nameof(batchId));
        return _repo.UpdatePullBatchRequestIdAsync(batchId, requestId, ct);
    }

    public Task AdvancePullCursorAsync(string sourceApi, DateTimeOffset beginAt, DateTimeOffset endAt, long batchId, string batchStatus, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        RequirePositiveId(batchId, nameof(batchId));
        if (endAt <= beginAt)
        {
            throw new ArgumentException("MSFX pull cursor end time must be later than begin time.", nameof(endAt));
        }

        if (string.IsNullOrWhiteSpace(batchStatus))
        {
            throw new ArgumentException("MSFX pull cursor batch status is required.", nameof(batchStatus));
        }

        return _repo.AdvancePullCursorAsync(sourceApi, beginAt, endAt, batchId, batchStatus.Trim(), ct);
    }

    public Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct)
        => _repo.GetRecentPullBatchesAsync(NormalizeLimit(limit), ct);
}
