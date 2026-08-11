using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;
using PacToolkits.Application.Serialization;

namespace PacToolkits.Desktop.Avalonia.Services.Infrastructure.Api;

/// <summary>经 PacApi 的 Dashboard</summary>
public sealed class ApiDashboard : IDashboardService
{
    private readonly PacApiClient _api;

    public ApiDashboard(PacApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public async Task<DashboardSnapshot> GetSnapshotAsync(DashboardRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri = BuildUri(
            "/v1/dashboard/snapshot",
            request.Filter,
            pairs:
            [
                ("refreshDistributions", request.RefreshDistributions ? "true" : "false"),
                ("overviewTopN", request.OverviewTopN.ToString(CultureInfo.InvariantCulture)),
                ("entryOverviewTopN", request.EntryOverviewTopN.ToString(CultureInfo.InvariantCulture)),
                ("txnPageIndex", request.TxnPageIndex.ToString(CultureInfo.InvariantCulture)),
                ("txnPageSize", request.TxnPageSize.ToString(CultureInfo.InvariantCulture)),
                ("txnTrendPageIndex", request.TxnTrendPageIndex.ToString(CultureInfo.InvariantCulture)),
                ("txnTrendPageSize", request.TxnTrendPageSize.ToString(CultureInfo.InvariantCulture)),
                ("entryPageIndex", request.EntryPageIndex.ToString(CultureInfo.InvariantCulture)),
                ("entryPageSize", request.EntryPageSize.ToString(CultureInfo.InvariantCulture)),
                ("abnormalPageIndex", request.AbnormalPageIndex.ToString(CultureInfo.InvariantCulture)),
                ("abnormalPageSize", request.AbnormalPageSize.ToString(CultureInfo.InvariantCulture)),
            ]);

        var body = await _api.GetJsonAsync(
                () => new HttpRequestMessage(HttpMethod.Get, uri),
                PacJsonContext.Default.DashboardSnapshotResponse,
                ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("empty dashboard snapshot response");

        return DashboardApiMapping.ToSnapshot(body);
    }

    public Task<PagedResult<TraceTxnDto>> GetTxnPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct)
        => GetPageAsync(
            "/v1/dashboard/transactions",
            filter,
            page,
            pageSize,
            PacJsonContext.Default.PagedResultTraceTxnDto,
            ct);

    public Task<PagedResult<TrendRowDto>> GetTxnTrendPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct)
        => GetPageAsync(
            "/v1/dashboard/trends",
            filter,
            page,
            pageSize,
            PacJsonContext.Default.PagedResultTrendRowDto,
            ct);

    public Task<PagedResult<TraceEntryLogDto>> GetEntryPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct)
        => GetPageAsync(
            "/v1/dashboard/entries",
            filter,
            page,
            pageSize,
            PacJsonContext.Default.PagedResultTraceEntryLogDto,
            ct);

    public Task<PagedResult<AbnormalRowDto>> GetAbnormalPageAsync(
        DashboardFilter filter,
        int page,
        int pageSize,
        CancellationToken ct)
        => GetPageAsync(
            "/v1/dashboard/abnormal",
            filter,
            page,
            pageSize,
            PacJsonContext.Default.PagedResultAbnormalRowDto,
            ct);

    private async Task<PagedResult<T>> GetPageAsync<T>(
        string path,
        DashboardFilter filter,
        int page,
        int pageSize,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<PagedResult<T>> typeInfo,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var uri = BuildUri(
            path,
            filter,
            pairs:
            [
                ("page", page.ToString(CultureInfo.InvariantCulture)),
                ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            ]);

        return await _api.GetJsonAsync(
                   () => new HttpRequestMessage(HttpMethod.Get, uri),
                   typeInfo,
                   ct)
               .ConfigureAwait(false)
               ?? throw new InvalidOperationException($"empty dashboard page response for {path}");
    }

    private Uri BuildUri(
        string path,
        DashboardFilter filter,
        IReadOnlyList<(string Key, string Value)> pairs)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = new List<string>
        {
            "from=" + Uri.EscapeDataString(filter.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "to=" + Uri.EscapeDataString(filter.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            "trendMetric=" + Uri.EscapeDataString(filter.TrendMetric.ToString()),
        };

        if (filter.ClientMachines is { Count: > 0 })
        {
            foreach (var machine in filter.ClientMachines)
            {
                if (!string.IsNullOrWhiteSpace(machine))
                {
                    query.Add("clientMachine=" + Uri.EscapeDataString(machine));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.DrugId))
        {
            query.Add("drugId=" + Uri.EscapeDataString(filter.DrugId));
        }

        if (!string.IsNullOrWhiteSpace(filter.Spec))
        {
            query.Add("spec=" + Uri.EscapeDataString(filter.Spec));
        }

        foreach (var (key, value) in pairs)
        {
            query.Add(key + "=" + Uri.EscapeDataString(value));
        }

        return _api.Resolve(path + "?" + string.Join('&', query));
    }
}
