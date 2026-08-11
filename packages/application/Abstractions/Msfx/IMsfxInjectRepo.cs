using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 注入任务建队与编排（重开/丢弃/拆并等）
/// </summary>
public interface IMsfxInjectRepo
{
    Task<MsfxBuildInject> BuildInjectsAsync(int maxGroups, CancellationToken ct);

    Task<IReadOnlyList<MsfxInjectQueueRow>> GetInjectQueueAsync(int limit, CancellationToken ct);

    Task<MsfxInjectReopen> ReopenInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);

    Task<MsfxInjectDiscard> DiscardInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);

    Task<MsfxInjectRemap> RemapInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);

    Task<MsfxInjectMerge> MergeInjectsAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct);

    Task<MsfxInjectSplit> SplitInjectAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct);

    Task<MsfxInjectSplitCustom> SplitInjectCustomAsync(
        long taskId,
        IReadOnlyList<string> groupKeys,
        IReadOnlyList<int> bucketIndexes,
        string? operatorName,
        string? reason,
        CancellationToken ct);

    Task<IReadOnlyList<MsfxInjectSplitUnitRow>> GetInjectSplitUnitsAsync(long taskId, CancellationToken ct);

    Task<IReadOnlyList<MsfxInjectSplitCodeRow>> GetInjectSplitCodeRowsAsync(long taskId, CancellationToken ct);
}
