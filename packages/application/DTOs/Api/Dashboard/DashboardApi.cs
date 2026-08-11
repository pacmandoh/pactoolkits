namespace PacToolkits.Application.DTOs;

/// <summary>快照中的客户端计数</summary>
public sealed record ClientCountDto(string Client, long Value);

/// <summary>药品目录或规格列表</summary>
public sealed record DashboardStringListResponse(IReadOnlyList<string> Items);

/// <summary>Dashboard 快照 HTTP 响应</summary>
public sealed record DashboardSnapshotResponse(
    IReadOnlyList<string> ClientNames,
    DashboardKpiDto Kpi,
    IReadOnlyList<TrendRowDto> Trend,
    PagedResult<TraceTxnDto> TxnsOverview,
    PagedResult<TraceTxnDto> TxnsPage,
    PagedResult<TrendRowDto> TxnTrendPage,
    PagedResult<TraceEntryLogDto> EntriesOverview,
    PagedResult<TraceEntryLogDto> EntriesPage,
    IReadOnlyList<ClientCountDto> TopClients,
    IReadOnlyList<TrendRowDto> ChartTrend,
    IReadOnlyList<TraceTxnDto> ChartTxns,
    bool DistributionsRefreshed,
    IReadOnlyList<ClientCountDto> ChartClients,
    IReadOnlyList<EntryChartRowDto> EntryChart,
    PagedResult<AbnormalRowDto> Abnormal);

public static class DashboardApiMapping
{
    public static DashboardSnapshotResponse FromSnapshot(DashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new DashboardSnapshotResponse(
            ClientNames: snapshot.ClientNames,
            Kpi: snapshot.Kpi,
            Trend: snapshot.Trend,
            TxnsOverview: snapshot.TxnsOverview,
            TxnsPage: snapshot.TxnsPage,
            TxnTrendPage: snapshot.TxnTrendPage,
            EntriesOverview: snapshot.EntriesOverview,
            EntriesPage: snapshot.EntriesPage,
            TopClients: ToCounts(snapshot.TopClients),
            ChartTrend: snapshot.ChartTrend,
            ChartTxns: snapshot.ChartTxns,
            DistributionsRefreshed: snapshot.DistributionsRefreshed,
            ChartClients: ToCounts(snapshot.ChartClients),
            EntryChart: snapshot.EntryChart,
            Abnormal: snapshot.Abnormal);
    }

    public static DashboardSnapshot ToSnapshot(DashboardSnapshotResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new DashboardSnapshot(
            ClientNames: response.ClientNames,
            Kpi: response.Kpi,
            Trend: response.Trend,
            TxnsOverview: response.TxnsOverview,
            TxnsPage: response.TxnsPage,
            TxnTrendPage: response.TxnTrendPage,
            EntriesOverview: response.EntriesOverview,
            EntriesPage: response.EntriesPage,
            TopClients: ToTuples(response.TopClients),
            ChartTrend: response.ChartTrend,
            ChartTxns: response.ChartTxns,
            DistributionsRefreshed: response.DistributionsRefreshed,
            ChartClients: ToTuples(response.ChartClients),
            EntryChart: response.EntryChart,
            Abnormal: response.Abnormal);
    }

    private static IReadOnlyList<ClientCountDto> ToCounts(IReadOnlyList<(string Client, long Value)> rows)
        => rows.Select(static row => new ClientCountDto(row.Client, row.Value)).ToArray();

    private static IReadOnlyList<(string Client, long Value)> ToTuples(IReadOnlyList<ClientCountDto> rows)
        => rows.Select(static row => (row.Client, row.Value)).ToArray();
}
