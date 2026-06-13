namespace PacToolkits.Application.DTOs;

public sealed record DashboardFilter(
    DateOnly From,
    DateOnly To,
    string? ClientRaw,
    string? DrugId,
    string? Spec,
    TrendMetric TrendMetric);

public sealed record DashboardLoadRequest(
    DashboardFilter Filter,
    int OverviewTopN,
    int EntryOverviewTopN,
    int TxnPageIndex,
    int TxnPageSize,
    int TxnTrendPageIndex,
    int TxnTrendPageSize,
    int EntryPageIndex,
    int EntryPageSize,
    int AbnormalPageIndex,
    int AbnormalPageSize);

public sealed record DashboardSnapshot(
    IReadOnlyList<string> ClientNames,
    DashboardKpiDto Kpi,
    IReadOnlyList<TrendRowDto> Trend,
    PagedResult<TraceTxnDto> TxnsOverview,
    PagedResult<TraceTxnDto> TxnsPage,
    PagedResult<TrendRowDto> TxnTrendPage,
    PagedResult<TraceEntryLogDto> EntriesOverview,
    PagedResult<TraceEntryLogDto> EntriesPage,
    IReadOnlyList<(string Client, long Value)> TopClients,
    PagedResult<AbnormalRowDto> Abnormal);
