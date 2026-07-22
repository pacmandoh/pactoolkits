using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services.Msfx;

public sealed partial class SyncService
{
    public Task<IReadOnlyList<MsfxInjectQueueRow>> GetInjectQueueAsync(int limit, CancellationToken ct)
        => _repo.GetInjectQueueAsync(limit < 0 ? 0 : limit, ct);

    public Task<MsfxInjectReopen> ReopenInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.ReopenInjectAsync(taskId, operatorName, reason, ct);
    }

    public Task<MsfxInjectDiscard> DiscardInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.DiscardInjectAsync(taskId, operatorName, reason, ct);
    }

    public Task<MsfxInjectRemap> RemapInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.RemapInjectAsync(taskId, operatorName, reason, ct);
    }

    public Task<MsfxInjectMerge> MergeInjectsAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taskIds);
        if (taskIds.Count < 2)
        {
            throw new ArgumentException("At least two MSFX tasks are required for merge.", nameof(taskIds));
        }

        if (taskIds.Any(x => x <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(taskIds), "MSFX task ids must be positive.");
        }

        return _repo.MergeInjectsAsync(taskIds, operatorName, reason, ct);
    }

    public Task<MsfxInjectSplit> SplitInjectAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        RequireText(splitMode, nameof(splitMode));
        return _repo.SplitInjectAsync(taskId, splitMode.Trim(), operatorName, reason, ct);
    }

    public Task<IReadOnlyList<MsfxInjectSplitUnitRow>> GetInjectSplitUnitsAsync(long taskId, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.GetInjectSplitUnitsAsync(taskId, ct);
    }

    public Task<IReadOnlyList<MsfxInjectSplitCodeRow>> GetInjectSplitCodeRowsAsync(long taskId, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        return _repo.GetInjectSplitCodeRowsAsync(taskId, ct);
    }

    public Task<MsfxInjectSplitCustom> SplitInjectCustomAsync(long taskId, IReadOnlyList<string> groupKeys, IReadOnlyList<int> bucketIndexes, string? operatorName, string? reason, CancellationToken ct)
    {
        RequirePositiveId(taskId, nameof(taskId));
        ArgumentNullException.ThrowIfNull(groupKeys);
        ArgumentNullException.ThrowIfNull(bucketIndexes);
        if (groupKeys.Count == 0 || bucketIndexes.Count == 0 || groupKeys.Count != bucketIndexes.Count)
        {
            throw new ArgumentException("Custom split requires matching group keys and bucket indexes.");
        }

        return _repo.SplitInjectCustomAsync(taskId, groupKeys, bucketIndexes, operatorName, reason, ct);
    }
}
