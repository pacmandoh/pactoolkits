using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// MSFX 同步业务契约：游标、看板查询、映射批处理与注入任务维护
/// </summary>
public interface ISyncService
{
    Task<MsfxPullCursorState> GetPullCursorAsync(string sourceApi, CancellationToken ct);

    Task<MsfxPullCursorState> AdvancePullCursorToAsync(string sourceApi, DateTimeOffset target, CancellationToken ct);

    Task<MsfxBuildInject> BuildInjectsAsync(int maxGroups, CancellationToken ct);

    Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct);

    Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct);

    Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
        int pageSize,
        IReadOnlyCollection<string>? mapStatuses,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        DateTimeOffset? cursorUpdatedAt,
        long? cursorId,
        bool newer,
        bool seekLastPage,
        CancellationToken ct);

    Task<IReadOnlyList<MsfxInjectQueueRow>> GetInjectQueueAsync(int limit, CancellationToken ct);

    Task<MsfxInjectReopen> ReopenInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);

    Task<MsfxInjectDiscard> DiscardInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);

    Task<MsfxInjectRemap> RemapInjectAsync(long taskId, string? operatorName, string? reason, CancellationToken ct);

    Task<MsfxInjectMerge> MergeInjectsAsync(IReadOnlyList<long> taskIds, string? operatorName, string? reason, CancellationToken ct);

    Task<MsfxInjectSplit> SplitInjectAsync(long taskId, string splitMode, string? operatorName, string? reason, CancellationToken ct);

    Task<IReadOnlyList<MsfxInjectSplitUnitRow>> GetInjectSplitUnitsAsync(long taskId, CancellationToken ct);

    Task<IReadOnlyList<MsfxInjectSplitCodeRow>> GetInjectSplitCodeRowsAsync(long taskId, CancellationToken ct);

    Task<MsfxInjectSplitCustom> SplitInjectCustomAsync(
        long taskId,
        IReadOnlyList<string> groupKeys,
        IReadOnlyList<int> bucketIndexes,
        string? operatorName,
        string? reason,
        CancellationToken ct);

    Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct);

    Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        int limit,
        CancellationToken ct);

    Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string? groupSourceDrugNameRaw,
        string? groupSourceSpecRaw,
        string? groupSourceNameNorm,
        string? groupSourceSpecNorm,
        string action,
        string? drugId,
        string? spec,
        CancellationToken ct);

    Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string? groupSourceDrugNameRaw,
        string? groupSourceSpecRaw,
        string? groupSourceNameNorm,
        string? groupSourceSpecNorm,
        string action,
        string? drugId,
        string? spec,
        CancellationToken ct);
}
