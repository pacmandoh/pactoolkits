using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 映射队列、自动映射与批量映射
/// </summary>
public interface IMsfxMappingRepo
{
    Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct);

    Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct);

    Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
        int pageSize,
        IReadOnlyCollection<string>? mapStatuses,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string[][]? pinyinExactPerToken,
        DateTimeOffset? cursorUpdatedAt,
        long? cursorId,
        bool newer,
        bool seekLastPage,
        CancellationToken ct);

    Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string[][]? pinyinExactPerToken,
        int limit,
        CancellationToken ct);

    Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string[][]? pinyinExactPerToken,
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
        string[][]? pinyinExactPerToken,
        string? groupSourceDrugNameRaw,
        string? groupSourceSpecRaw,
        string? groupSourceNameNorm,
        string? groupSourceSpecNorm,
        string action,
        string? drugId,
        string? spec,
        CancellationToken ct);
}
