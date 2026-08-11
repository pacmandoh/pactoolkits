using PacToolkits.Api.Auth;
using PacToolkits.Api.Hosting;
using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Api.Endpoints;

/// <summary>Dashboard 只读查询</summary>
public static class DashboardEndpoints
{
    private const int MaxPageSize = 200;
    private const int MaxTopN = 200;
    private const int MaxPageIndex = 10_000;

    public static IEndpointRouteBuilder MapDashboard(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/v1/dashboard/snapshot", GetSnapshot)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/dashboard/transactions", GetTransactions)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/dashboard/trends", GetTrends)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/dashboard/entries", GetEntries)
            .RequireAuthorization(AuthPolicies.Read);
        routes.MapGet("/v1/dashboard/abnormal", GetAbnormal)
            .RequireAuthorization(AuthPolicies.Read);
        return routes;
    }

    private static async Task<IResult> GetSnapshot(
        HttpContext http,
        IDashboardService dashboard,
        CancellationToken ct)
    {
        if (!TryBindFilter(http.Request, out var filter, out var error))
        {
            return error!;
        }

        if (!TryBindSnapshotRequest(http.Request, filter, out var request, out error))
        {
            return error!;
        }

        var snapshot = await dashboard.GetSnapshotAsync(request, ct).ConfigureAwait(false);
        return Results.Ok(DashboardApiMapping.FromSnapshot(snapshot));
    }

    private static async Task<IResult> GetTransactions(
        HttpContext http,
        IDashboardService dashboard,
        CancellationToken ct)
        => await GetPageAsync(http, dashboard.GetTxnPageAsync, ct).ConfigureAwait(false);

    private static async Task<IResult> GetTrends(
        HttpContext http,
        IDashboardService dashboard,
        CancellationToken ct)
        => await GetPageAsync(http, dashboard.GetTxnTrendPageAsync, ct).ConfigureAwait(false);

    private static async Task<IResult> GetEntries(
        HttpContext http,
        IDashboardService dashboard,
        CancellationToken ct)
        => await GetPageAsync(http, dashboard.GetEntryPageAsync, ct).ConfigureAwait(false);

    private static async Task<IResult> GetAbnormal(
        HttpContext http,
        IDashboardService dashboard,
        CancellationToken ct)
        => await GetPageAsync(http, dashboard.GetAbnormalPageAsync, ct).ConfigureAwait(false);

    private static async Task<IResult> GetPageAsync<T>(
        HttpContext http,
        Func<DashboardFilter, int, int, CancellationToken, Task<PagedResult<T>>> load,
        CancellationToken ct)
    {
        if (!TryBindFilter(http.Request, out var filter, out var error))
        {
            return error!;
        }

        if (!TryBindPage(http.Request, out var page, out var pageSize, out error))
        {
            return error!;
        }

        var result = await load(filter, page, pageSize, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static bool TryBindFilter(HttpRequest request, out DashboardFilter filter, out IResult? error)
    {
        filter = default!;
        error = null;

        if (!TryParseDate(request, "from", out var from, out error)
            || !TryParseDate(request, "to", out var to, out error))
        {
            return false;
        }

        if (from > to)
        {
            error = ApiProblems.BadRequest(
                request.HttpContext,
                title: "Invalid date range",
                detail: "Query from must be on or before to");
            return false;
        }

        if (!TryParseTrendMetric(request, out var metric, out error))
        {
            return false;
        }

        var machines = request.Query["clientMachine"]
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .ToArray();

        filter = new DashboardFilter(
            From: from,
            To: to,
            ClientMachines: machines.Length == 0 ? null : machines,
            DrugId: NullIfWhiteSpace(request.Query["drugId"]),
            Spec: NullIfWhiteSpace(request.Query["spec"]),
            TrendMetric: metric);
        return true;
    }

    private static bool TryBindSnapshotRequest(
        HttpRequest request,
        DashboardFilter filter,
        out DashboardRequest snapshotRequest,
        out IResult? error)
    {
        snapshotRequest = default!;
        error = null;

        if (!TryParseBool(request, "refreshDistributions", defaultValue: false, out var refresh, out error)
            || !TryParseBoundedPositiveInt(
                request, "overviewTopN", defaultValue: 10, max: MaxTopN, out var overviewTopN, out error)
            || !TryParseBoundedPositiveInt(
                request, "entryOverviewTopN", defaultValue: 6, max: MaxTopN, out var entryOverviewTopN, out error)
            || !TryParseBoundedPositiveInt(
                request, "txnPageIndex", defaultValue: 1, max: MaxPageIndex, out var txnPageIndex, out error)
            || !TryParsePageSize(request, "txnPageSize", defaultValue: 20, out var txnPageSize, out error)
            || !TryParseBoundedPositiveInt(
                request, "txnTrendPageIndex", defaultValue: 1, max: MaxPageIndex, out var txnTrendPageIndex, out error)
            || !TryParsePageSize(request, "txnTrendPageSize", defaultValue: 20, out var txnTrendPageSize, out error)
            || !TryParseBoundedPositiveInt(
                request, "entryPageIndex", defaultValue: 1, max: MaxPageIndex, out var entryPageIndex, out error)
            || !TryParsePageSize(request, "entryPageSize", defaultValue: 20, out var entryPageSize, out error)
            || !TryParseBoundedPositiveInt(
                request, "abnormalPageIndex", defaultValue: 1, max: MaxPageIndex, out var abnormalPageIndex, out error)
            || !TryParsePageSize(request, "abnormalPageSize", defaultValue: 20, out var abnormalPageSize, out error))
        {
            return false;
        }

        snapshotRequest = new DashboardRequest(
            Filter: filter,
            RefreshDistributions: refresh,
            OverviewTopN: overviewTopN,
            EntryOverviewTopN: entryOverviewTopN,
            TxnPageIndex: txnPageIndex,
            TxnPageSize: txnPageSize,
            TxnTrendPageIndex: txnTrendPageIndex,
            TxnTrendPageSize: txnTrendPageSize,
            EntryPageIndex: entryPageIndex,
            EntryPageSize: entryPageSize,
            AbnormalPageIndex: abnormalPageIndex,
            AbnormalPageSize: abnormalPageSize);
        return true;
    }

    private static bool TryBindPage(
        HttpRequest request,
        out int page,
        out int pageSize,
        out IResult? error)
    {
        page = 0;
        pageSize = 0;
        return TryParseBoundedPositiveInt(request, "page", defaultValue: 1, max: MaxPageIndex, out page, out error)
               && TryParsePageSize(request, "pageSize", defaultValue: 20, out pageSize, out error);
    }

    private static bool TryParseDate(
        HttpRequest request,
        string name,
        out DateOnly value,
        out IResult? error)
    {
        value = default;
        error = null;
        var raw = request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = ApiProblems.BadRequest(
                request.HttpContext,
                title: "Missing query",
                detail: $"Query {name} is required");
            return false;
        }

        if (!DateOnly.TryParse(raw, out value))
        {
            error = ApiProblems.BadRequest(
                request.HttpContext,
                title: "Invalid query",
                detail: $"Query {name} must be a date (yyyy-MM-dd)");
            return false;
        }

        return true;
    }

    private static bool TryParseTrendMetric(
        HttpRequest request,
        out TrendMetric metric,
        out IResult? error)
    {
        metric = TrendMetric.Qty;
        error = null;
        var raw = request.Query["trendMetric"].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (Enum.TryParse(raw, ignoreCase: true, out metric)
            && Enum.IsDefined(metric))
        {
            return true;
        }

        error = ApiProblems.BadRequest(
            request.HttpContext,
            title: "Invalid query",
            detail: "Query trendMetric must be Qty or Txn");
        return false;
    }

    private static bool TryParseBool(
        HttpRequest request,
        string name,
        bool defaultValue,
        out bool value,
        out IResult? error)
    {
        value = defaultValue;
        error = null;
        var raw = request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (bool.TryParse(raw, out value))
        {
            return true;
        }

        error = ApiProblems.BadRequest(
            request.HttpContext,
            title: "Invalid query",
            detail: $"Query {name} must be true or false");
        return false;
    }

    private static bool TryParseBoundedPositiveInt(
        HttpRequest request,
        string name,
        int defaultValue,
        int max,
        out int value,
        out IResult? error)
    {
        value = defaultValue;
        error = null;
        var raw = request.Query[name].ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!int.TryParse(raw, out value) || value <= 0)
        {
            error = ApiProblems.BadRequest(
                request.HttpContext,
                title: "Invalid query",
                detail: $"Query {name} must be a positive integer");
            return false;
        }

        if (value > max)
        {
            error = ApiProblems.BadRequest(
                request.HttpContext,
                title: "Invalid query",
                detail: $"Query {name} must be <= {max}");
            return false;
        }

        return true;
    }

    private static bool TryParsePageSize(
        HttpRequest request,
        string name,
        int defaultValue,
        out int value,
        out IResult? error)
        => TryParseBoundedPositiveInt(request, name, defaultValue, MaxPageSize, out value, out error);

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
