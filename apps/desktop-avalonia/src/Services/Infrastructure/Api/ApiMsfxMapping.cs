using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>AutoRun 只用映射快照与自动映射；队列走 <see cref="ApiSync"/></summary>
public sealed class ApiMsfxMapping : IMsfxMappingRepo
{
    private readonly ISyncService _sync;

    public ApiMsfxMapping(ISyncService sync)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
    }

    public Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct)
        => _sync.ApplyMappingAsync(limit, ct);

    public Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct)
        => _sync.GetMappingStatusSnapshotAsync(ct);

    public Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
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
        CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        string[][]? pinyinExactPerToken,
        int limit,
        CancellationToken ct)
        => throw new NotSupportedException();

    public Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(
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
        CancellationToken ct)
        => throw new NotSupportedException();

    public Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(
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
        CancellationToken ct)
        => throw new NotSupportedException();
}
