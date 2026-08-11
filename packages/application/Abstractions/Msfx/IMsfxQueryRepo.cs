using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 看板与批次审计等只读查询
/// </summary>
public interface IMsfxQueryRepo
{
    Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct);

    Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct);
}
