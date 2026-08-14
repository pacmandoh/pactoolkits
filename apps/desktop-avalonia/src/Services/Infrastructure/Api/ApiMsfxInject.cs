using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>AutoRun 只用建任务；队列编排走 <see cref="ApiSync"/></summary>
public sealed class ApiMsfxInject : IMsfxInjectRepo
{
    private readonly ISyncService _sync;

    public ApiMsfxInject(ISyncService sync)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
    }

    public Task<MsfxBuildInject> BuildInjectsAsync(int maxGroups, CancellationToken ct)
        => _sync.BuildInjectsAsync(maxGroups, ct);

    public Task<IReadOnlyList<MsfxInjectQueueRow>> GetInjectQueueAsync(int limit, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<MsfxInjectReopen> ReopenInjectAsync(
        long taskId,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => throw new NotSupportedException();

    public Task<MsfxInjectDiscard> DiscardInjectAsync(
        long taskId,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => throw new NotSupportedException();

    public Task<MsfxInjectRemap> RemapInjectAsync(
        long taskId,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => throw new NotSupportedException();

    public Task<MsfxInjectMerge> MergeInjectsAsync(
        IReadOnlyList<long> taskIds,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => throw new NotSupportedException();

    public Task<MsfxInjectSplit> SplitInjectAsync(
        long taskId,
        string splitMode,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => throw new NotSupportedException();

    public Task<MsfxInjectSplitCustom> SplitInjectCustomAsync(
        long taskId,
        IReadOnlyList<string> groupKeys,
        IReadOnlyList<int> bucketIndexes,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<MsfxInjectSplitUnitRow>> GetInjectSplitUnitsAsync(long taskId, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<MsfxInjectSplitCodeRow>> GetInjectSplitCodeRowsAsync(long taskId, CancellationToken ct)
        => throw new NotSupportedException();
}
