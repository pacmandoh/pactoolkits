using System;
using System.Collections.Generic;

namespace pactoolkits_ui.Contracts;


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
    Failed = 3
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
    int Qty,
    DateTimeOffset CreatedAt,
    string? ClientName
);

public sealed record DashboardKpiDto(
    long AvailableRemain,
    long PeriodUsed,
    long Abnormal,
    long LowStockCount,

    long TotalQty,         // Wave 分母：sum(trace_pool.qty)（同筛选条件）
    long TotalTxnCount,    // Wave 分母：count(trace_txn)（同筛选条件，排除 PENDING）
    long TotalDrugCount,   // Wave 分母：count(distinct drug_id+spec)（同筛选条件）
    long? SelectedPoolCount,        // N
    long? SelectedZeroRemainCount   // M
);

public sealed record ClientInfo(
    string Raw,          // 原始 client 字符串（SQL 查询用）
    string Display,      // UI 主显示

    string? Machine,
    string? User,

    string? Ip,
    string? Os,
    string? Version
);

public sealed record TraceEntryLogDto(
    DateTimeOffset EntryAt,

    string DrugId,          // 药品名
    string Spec,            // 规格

    int EntryCount,         // 录入追溯码条数
    int QtyPerTrace,        // 每条追溯码可用次数
    int TotalAvailableQty,  // EntryCount × QtyPerTrace

    int FailedCount,        // 本次录入失败的追溯码条数
    string Result,          // success / partial / failed

    long? TxnId,            // 关联的 trace_txn.id（success / partial 时有）
    string Client,          // 客户端（原始 client 协议字符串）
    string Source,          // manual / batch / api

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
    Qty = 0, // sum(req_qty)
    Txn = 1  // count(*) i.e. 事务次数
}

public sealed record DashboardQuery(
    DateRange Range,
    string? ClientName,

    string? DrugId = null,   // 新增：药品名（来自 drug_index.drug_id）
    string? Spec = null,     // 新增：规格（来自 drug_index.spec）

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
