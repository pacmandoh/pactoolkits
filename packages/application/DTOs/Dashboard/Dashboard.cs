namespace PacToolkits.Application.DTOs;

public enum TxnBadge
{
    Unknown = 0,
    Done = 1,
    Warning = 2,
    Danger = 3
}

/// <summary>
/// 追溯录入流水状态（含人工复核等）
/// </summary>
public enum TraceEntryState
{
    Unknown = 0,
    Success = 1,
    Warning = 2,
    Failed = 3,
    Info = 4,
    Discarded = 5,
    ManualReview = 6
}

public enum TxnStatus
{
    Commit = 1,
    Rollback = 2,
    Pending = 3
}

/// <summary>
/// 药品索引行（drugId+spec 主键与版本）
/// </summary>
public sealed record DrugIndexDto(
    string DrugId,
    string Spec,
    int Qty,
    string? RuleKey,
    string? PreTc,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    long Version
);

public sealed record TraceTxnDto(
    long Id,
    TxnStatus Status,
    TxnBadge Badge,
    string Title,
    string DrugId,
    string Spec,
    int Qty,
    DateTimeOffset CreatedAt,
    string? ClientRaw
);

public sealed record DashboardKpiDto(
    long AvailableRemain,
    long PeriodUsed,
    long Abnormal,
    long LowStockCount,
    long TotalQty,
    long TotalTxnCount,
    long TotalDrugCount,
    long? SelectedPoolCount,
    long? SelectedZeroRemainCount
);

/// <summary>
/// 客户端展示信息（含别名后的 Display）
/// </summary>
public sealed record ClientInfo(
    string Raw,
    string Display,
    string? Machine,
    string? User,
    string? Ip,
    string? Os,
    string? Version,
    IReadOnlyList<string>? Machines = null)
{
    public IReadOnlyList<string> MachineKeys
    {
        get
        {
            if (Machines is { Count: > 0 })
            {
                return Machines;
            }

            var key = !string.IsNullOrWhiteSpace(Machine)
                ? Machine
                : string.IsNullOrWhiteSpace(Raw)
                    ? null
                    : Raw.Split('|', 2, StringSplitOptions.TrimEntries)[0];
            return string.IsNullOrWhiteSpace(key) ? [] : [key];
        }
    }

    public bool ContainsMachine(string? machine)
        => !string.IsNullOrWhiteSpace(machine)
           && MachineKeys.Any(key => string.Equals(key, machine, StringComparison.OrdinalIgnoreCase));
}

public sealed record TraceEntryLogDto(
    DateTimeOffset EntryAt,
    string DrugId,
    string Spec,
    int EntryCount,
    int QtyPerTrace,
    int TotalAvailableQty,
    int FailedCount,
    string Result,
    long? TxnId,
    string Client,
    string Source,
    string? Message
);

public sealed record EntryChartRowDto(
    string ClientRaw,
    TraceEntryState State,
    long Count);

public sealed record TrendRowDto(
    int Rank,
    string Name,
    string Sub,
    string? TopClientRaw,
    decimal TopClientPct,
    string ValueText
);

public sealed record AbnormalRowDto(
    string Title,
    string Detail,
    string ClientRaw,
    TxnBadge Badge,
    long? TxnId
);

public sealed record DateRange(DateOnly From, DateOnly To);

/// <summary>
/// 趋势图度量口径
/// </summary>
public enum TrendMetric
{
    Qty = 0,
    Txn = 1
}

public sealed record DashboardQuery(
    DateRange Range,
    IReadOnlyList<string> ClientMachines,
    string? DrugId = null,
    string? Spec = null,
    int TopN = 10,
    TrendMetric TrendMetric = TrendMetric.Qty
);

public sealed record TracePoolStockRowDto(
    string DrugId,
    string Spec,
    string TraceCode,
    int Qty,
    int Remain,
    int Status,
    long Version,
    bool IsLow,
    bool IsDeprecated = false
);

public sealed record TracePoolDrugSpecAggDto(
    string DrugId,
    string Spec,
    long CodeCount,
    long QtySum,
    long RemainSum,
    long WeekUsed,
    decimal Threshold,
    bool IsLow,
    bool IsDeprecated = false
);

public sealed record LowStockRowDto(
    string DrugId,
    string Spec,
    long RemainSum,
    decimal Threshold,
    bool IsLow
);

public sealed record MissingInventoryRowDto(
    string DrugId,
    string Spec,
    string? Note
);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Rows,
    int TotalCount
);

public sealed record StockReassignPreviewDto(
    bool TargetExists,
    int MatchCount,
    int WillChangeCount,
    string? CurrentDrugId,
    string? CurrentSpec,
    bool IsNoopTarget,
    IReadOnlyList<StockReassignPreviewItemDto> Samples
);

public sealed record StockReassignApplyResultDto(
    int AffectedRows,
    long AuditId
);

public sealed record StockReassignPreviewItemDto(
    string TraceCode,
    string DrugId,
    string Spec,
    int Qty,
    int Remain
);

/// <summary>
/// 药品主键修复预览
/// </summary>
public sealed record DrugKeyFixPreviewDto(
    bool SourceExists,
    bool TargetExists,
    int TracePoolAffected,
    int TraceTxnAffected,
    int MsfxAffected
);

/// <summary>
/// 药品主键修复应用结果
/// </summary>
public sealed record DrugKeyFixApplyResultDto(
    bool TargetExisted,
    int TracePoolAffected,
    int TraceTxnAffected,
    int MsfxAffected,
    long AuditId,
    DrugIndexDto Current
);

public sealed record DashboardFilter(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<string>? ClientMachines,
    string? DrugId,
    string? Spec,
    TrendMetric TrendMetric);

public sealed record DashboardRequest(
    DashboardFilter Filter,
    bool RefreshDistributions,
    int OverviewTopN,
    int EntryOverviewTopN,
    int TxnPageIndex,
    int TxnPageSize,
    int TxnTrendPageIndex,
    int TxnTrendPageSize,
    int EntryPageIndex,
    int EntryPageSize,
    int AbnormalPageIndex,
    int AbnormalPageSize,
    bool RefreshClientNames);

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
    IReadOnlyList<TrendRowDto> ChartTrend,
    IReadOnlyList<TraceTxnDto> ChartTxns,
    bool DistributionsRefreshed,
    IReadOnlyList<(string Client, long Value)> ChartClients,
    IReadOnlyList<EntryChartRowDto> EntryChart,
    PagedResult<AbnormalRowDto> Abnormal);
