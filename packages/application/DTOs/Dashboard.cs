namespace PacToolkits.Application.DTOs;

public enum TxnBadge
{
    Unknown = 0,
    Done = 1,
    Warning = 2,
    Danger = 3
}

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
    string? ClientName
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

public sealed record ClientInfo(
    string Raw,
    string Display,
    string? Machine,
    string? User,
    string? Ip,
    string? Os,
    string? Version
);

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
    string ClientDisplay,
    TxnBadge Badge,
    long? TxnId
);

public sealed record DateRange(DateOnly From, DateOnly To);

public enum TrendMetric
{
    Qty = 0,
    Txn = 1
}

public sealed record DashboardQuery(
    DateRange Range,
    string? ClientName,
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

public sealed record DrugKeyFixPreviewDto(
    bool SourceExists,
    bool TargetExists,
    int TracePoolAffected,
    int TraceTxnAffected
);

public sealed record DrugKeyFixApplyResultDto(
    bool TargetExisted,
    int TracePoolAffected,
    int TraceTxnAffected,
    long AuditId,
    DrugIndexDto Current
);

public sealed record DashboardFilter(
    DateOnly From,
    DateOnly To,
    string? ClientRaw,
    string? DrugId,
    string? Spec,
    TrendMetric TrendMetric);

public sealed record DashboardRequest(
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
