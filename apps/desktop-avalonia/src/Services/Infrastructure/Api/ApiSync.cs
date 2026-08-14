using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>MSFX 库侧同步走 HTTP（看板、游标、映射、注入）</summary>
public sealed class ApiSync : ISyncService
{
    private readonly PacApiClient _api;

    public ApiSync(PacApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<MsfxAutoBoardSnapshot> GetAutoBoardSnapshotAsync(CancellationToken ct)
        => await _api.GetJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/msfx/board")),
                   PacJsonContext.Default.MsfxAutoBoardSnapshot,
                   ct)
               .ConfigureAwait(false)
           ?? throw new InvalidOperationException("empty msfx board response");

    public async Task<IReadOnlyList<MsfxPullBatchRow>> GetRecentPullBatchesAsync(int limit, CancellationToken ct)
    {
        var uri = _api.Resolve(
            "/v1/msfx/pull-batches?limit="
            + Uri.EscapeDataString(limit.ToString(CultureInfo.InvariantCulture)));
        var items = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, uri),
                PacJsonContext.Default.MsfxPullBatchRowArray,
                ct)
            .ConfigureAwait(false);
        return items ?? Array.Empty<MsfxPullBatchRow>();
    }

    public async Task<MsfxPullCursorState> GetPullCursorAsync(string sourceApi, CancellationToken ct)
    {
        var uri = _api.Resolve(
            "/v1/msfx/cursor?sourceApi=" + Uri.EscapeDataString(sourceApi ?? string.Empty));
        return await _api.GetJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Get, uri),
                   PacJsonContext.Default.MsfxPullCursorState,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty msfx cursor response");
    }

    public async Task<MsfxPullCursorState> AdvancePullCursorToAsync(
        string sourceApi,
        DateTimeOffset target,
        CancellationToken ct)
    {
        var request = new MsfxCursorAdvanceRequest(sourceApi, target);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxCursorAdvanceRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/msfx/cursor/advance"))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.MsfxPullCursorState,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty msfx cursor advance response");
    }

    public async Task<MsfxMappingQueuePage> GetMappingQueuePageAsync(
        int pageSize,
        IReadOnlyCollection<string>? mapStatuses,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        DateTimeOffset? cursorUpdatedAt,
        long? cursorId,
        bool newer,
        bool seekLastPage,
        CancellationToken ct)
    {
        var query = new List<string>
        {
            "pageSize=" + Uri.EscapeDataString(pageSize.ToString(CultureInfo.InvariantCulture)),
            "newer=" + (newer ? "true" : "false"),
            "seekLastPage=" + (seekLastPage ? "true" : "false"),
        };
        if (mapStatuses is { Count: > 0 })
        {
            query.Add("mapStatuses=" + Uri.EscapeDataString(string.Join(',', mapStatuses)));
        }

        if (!string.IsNullOrWhiteSpace(codeStatus))
        {
            query.Add("codeStatus=" + Uri.EscapeDataString(codeStatus));
        }

        if (!string.IsNullOrWhiteSpace(searchScope))
        {
            query.Add("searchScope=" + Uri.EscapeDataString(searchScope));
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query.Add("keyword=" + Uri.EscapeDataString(keyword));
        }

        if (cursorUpdatedAt is { } updatedAt)
        {
            query.Add(
                "cursorUpdatedAt="
                + Uri.EscapeDataString(updatedAt.ToString("O", CultureInfo.InvariantCulture)));
        }

        if (cursorId is { } id)
        {
            query.Add("cursorId=" + Uri.EscapeDataString(id.ToString(CultureInfo.InvariantCulture)));
        }

        return await _api.GetJsonAsync(
                   () => new HttpRequestMessage(
                       HttpMethod.Get,
                       _api.Resolve("/v1/msfx/mapping/queue?" + string.Join('&', query))),
                   PacJsonContext.Default.MsfxMappingQueuePage,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty msfx mapping queue response");
    }

    public async Task<MsfxMappingStatusSnapshot> GetMappingStatusSnapshotAsync(CancellationToken ct)
        => await _api.GetJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve("/v1/msfx/mapping/status")),
                   PacJsonContext.Default.MsfxMappingStatusSnapshot,
                   ct)
               .ConfigureAwait(false)
           ?? throw new InvalidOperationException("empty msfx mapping status response");

    public async Task<MsfxMapApplyResult> ApplyMappingAsync(int limit, CancellationToken ct)
    {
        var request = new MsfxMappingApplyRequest(limit);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxMappingApplyRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/msfx/mapping/apply"))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.MsfxMapApplyResult,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty msfx mapping apply response");
    }

    public async Task<IReadOnlyList<MsfxMappingBatchGroupRow>> GetMappingBatchGroupsAsync(
        string? mapStatus,
        string? codeStatus,
        string? searchScope,
        string? keyword,
        int limit,
        CancellationToken ct)
    {
        var query = new List<string>
        {
            "limit=" + Uri.EscapeDataString(limit.ToString(CultureInfo.InvariantCulture)),
        };
        AppendOptional(query, "mapStatus", mapStatus);
        AppendOptional(query, "codeStatus", codeStatus);
        AppendOptional(query, "searchScope", searchScope);
        AppendOptional(query, "keyword", keyword);

        var items = await _api.GetJsonAsync(
                () => new HttpRequestMessage(
                    HttpMethod.Get,
                    _api.Resolve("/v1/msfx/mapping-batch/groups?" + string.Join('&', query))),
                PacJsonContext.Default.MsfxMappingBatchGroupRowArray,
                ct)
            .ConfigureAwait(false);
        return items ?? Array.Empty<MsfxMappingBatchGroupRow>();
    }

    public Task<MsfxMappingBatchPreview> PreviewMappingBatchByGroupAsync(
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
        CancellationToken ct)
        => PostMappingBatchAsync(
            "/v1/msfx/mapping-batch/preview",
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            groupSourceDrugNameRaw,
            groupSourceSpecRaw,
            groupSourceNameNorm,
            groupSourceSpecNorm,
            action,
            drugId,
            spec,
            PacJsonContext.Default.MsfxMappingBatchPreview,
            "empty msfx mapping-batch preview response",
            ct);

    public Task<MsfxMappingBatchApplyResult> ApplyMappingBatchByGroupAsync(
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
        CancellationToken ct)
        => PostMappingBatchAsync(
            "/v1/msfx/mapping-batch/commit",
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            groupSourceDrugNameRaw,
            groupSourceSpecRaw,
            groupSourceNameNorm,
            groupSourceSpecNorm,
            action,
            drugId,
            spec,
            PacJsonContext.Default.MsfxMappingBatchApplyResult,
            "empty msfx mapping-batch commit response",
            ct);

    public async Task<IReadOnlyList<MsfxInjectQueueRow>> GetInjectQueueAsync(int limit, CancellationToken ct)
    {
        var uri = _api.Resolve(
            "/v1/msfx/inject/queue?limit="
            + Uri.EscapeDataString(limit.ToString(CultureInfo.InvariantCulture)));
        var items = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, uri),
                PacJsonContext.Default.MsfxInjectQueueRowArray,
                ct)
            .ConfigureAwait(false);
        return items ?? Array.Empty<MsfxInjectQueueRow>();
    }

    public async Task<MsfxBuildInject> BuildInjectsAsync(int maxGroups, CancellationToken ct)
    {
        var request = new MsfxInjectBuildRequest(maxGroups);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxInjectBuildRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/msfx/inject/build"))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.MsfxBuildInject,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty msfx inject build response");
    }

    public Task<MsfxInjectReopen> ReopenInjectAsync(
        long taskId,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => PostInjectReasonAsync(
            taskId,
            "reopen",
            operatorName,
            reason,
            PacJsonContext.Default.MsfxInjectReopen,
            "empty msfx inject reopen response",
            ct);

    public Task<MsfxInjectDiscard> DiscardInjectAsync(
        long taskId,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => PostInjectReasonAsync(
            taskId,
            "discard",
            operatorName,
            reason,
            PacJsonContext.Default.MsfxInjectDiscard,
            "empty msfx inject discard response",
            ct);

    public Task<MsfxInjectRemap> RemapInjectAsync(
        long taskId,
        string? operatorName,
        string? reason,
        CancellationToken ct)
        => PostInjectReasonAsync(
            taskId,
            "remap",
            operatorName,
            reason,
            PacJsonContext.Default.MsfxInjectRemap,
            "empty msfx inject remap response",
            ct);

    public async Task<MsfxInjectMerge> MergeInjectsAsync(
        IReadOnlyList<long> taskIds,
        string? operatorName,
        string? reason,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(taskIds);
        var request = new MsfxInjectMergeRequest(taskIds, operatorName, reason);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxInjectMergeRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve("/v1/msfx/inject/merge"))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.MsfxInjectMerge,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty msfx inject merge response");
    }

    public async Task<MsfxInjectSplit> SplitInjectAsync(
        long taskId,
        string splitMode,
        string? operatorName,
        string? reason,
        CancellationToken ct)
    {
        var request = new MsfxInjectSplitRequest(splitMode, operatorName, reason);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxInjectSplitRequest);
        var path = "/v1/msfx/inject/"
                   + Uri.EscapeDataString(taskId.ToString(CultureInfo.InvariantCulture))
                   + "/split";
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve(path))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.MsfxInjectSplit,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty msfx inject split response");
    }

    public async Task<IReadOnlyList<MsfxInjectSplitUnitRow>> GetInjectSplitUnitsAsync(
        long taskId,
        CancellationToken ct)
    {
        var path = "/v1/msfx/inject/"
                   + Uri.EscapeDataString(taskId.ToString(CultureInfo.InvariantCulture))
                   + "/split-units";
        var items = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve(path)),
                PacJsonContext.Default.MsfxInjectSplitUnitRowArray,
                ct)
            .ConfigureAwait(false);
        return items ?? Array.Empty<MsfxInjectSplitUnitRow>();
    }

    public async Task<IReadOnlyList<MsfxInjectSplitCodeRow>> GetInjectSplitCodeRowsAsync(
        long taskId,
        CancellationToken ct)
    {
        var path = "/v1/msfx/inject/"
                   + Uri.EscapeDataString(taskId.ToString(CultureInfo.InvariantCulture))
                   + "/split-codes";
        var items = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _api.Resolve(path)),
                PacJsonContext.Default.MsfxInjectSplitCodeRowArray,
                ct)
            .ConfigureAwait(false);
        return items ?? Array.Empty<MsfxInjectSplitCodeRow>();
    }

    public async Task<MsfxInjectSplitCustom> SplitInjectCustomAsync(
        long taskId,
        IReadOnlyList<string> groupKeys,
        IReadOnlyList<int> bucketIndexes,
        string? operatorName,
        string? reason,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(groupKeys);
        ArgumentNullException.ThrowIfNull(bucketIndexes);
        var request = new MsfxInjectSplitCustomRequest(groupKeys, bucketIndexes, operatorName, reason);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxInjectSplitCustomRequest);
        var path = "/v1/msfx/inject/"
                   + Uri.EscapeDataString(taskId.ToString(CultureInfo.InvariantCulture))
                   + "/split-custom";
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve(path))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   PacJsonContext.Default.MsfxInjectSplitCustom,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException("empty msfx inject split-custom response");
    }

    private async Task<T> PostMappingBatchAsync<T>(
        string path,
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
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        string emptyMessage,
        CancellationToken ct)
    {
        var request = new MsfxMappingBatchRequest(
            mapStatus,
            codeStatus,
            searchScope,
            keyword,
            groupSourceDrugNameRaw,
            groupSourceSpecRaw,
            groupSourceNameNorm,
            groupSourceSpecNorm,
            action,
            drugId,
            spec);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxMappingBatchRequest);
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve(path))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   typeInfo,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException(emptyMessage);
    }

    private async Task<T> PostInjectReasonAsync<T>(
        long taskId,
        string action,
        string? operatorName,
        string? reason,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        string emptyMessage,
        CancellationToken ct)
    {
        var request = new MsfxInjectReasonRequest(operatorName, reason);
        var json = JsonSerializer.Serialize(request, PacJsonContext.Default.MsfxInjectReasonRequest);
        var path = "/v1/msfx/inject/"
                   + Uri.EscapeDataString(taskId.ToString(CultureInfo.InvariantCulture))
                   + "/"
                   + action;
        return await _api.PostJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Post, _api.Resolve(path))
                   {
                       Content = new StringContent(json, Encoding.UTF8, "application/json"),
                   },
                   typeInfo,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException(emptyMessage);
    }

    private static void AppendOptional(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add(name + "=" + Uri.EscapeDataString(value));
        }
    }
}
