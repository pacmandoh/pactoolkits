using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

public sealed partial class SyncService
{
    public Task<MsfxPullCursorState> GetPullCursorAsync(string sourceApi, CancellationToken ct)
    {
        RequireSourceApi(sourceApi);
        return _pull.GetPullCursorAsync(sourceApi, ct);
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
            await _pull.TryAcquireRunLockAsync(sourceApi, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("MSFX 自动巡检正在运行，暂时不能调整拉取游标");

        var current = await _pull.GetPullCursorAsync(sourceApi, ct).ConfigureAwait(false);
        if (current.LastSuccessEnd is { } currentEnd && target <= currentEnd)
        {
            throw new InvalidOperationException("目标游标必须晚于当前游标；设置页只允许向前跳过数据");
        }

        if (!await _pull.AdvancePullCursorToAsync(sourceApi, target, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("拉取游标未更新，请刷新当前游标后重试");
        }

        return await _pull.GetPullCursorAsync(sourceApi, ct).ConfigureAwait(false);
    }

    public Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct)
        => _query.GetAutoBoardSnapshotAsync(ct);

    public Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct)
        => _query.GetRecentPullBatchesAsync(NormalizeLimit(limit), ct);
}
