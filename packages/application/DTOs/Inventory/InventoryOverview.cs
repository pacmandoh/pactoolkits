namespace PacToolkits.Application.DTOs;

/// <summary>
/// 按行聚合的库存保存请求（同一 trace 的多列一次 UPDATE）
/// </summary>
public sealed record StockRowEditRequest(
    string MatchTraceCode,
    long ExpectedVersion,
    string? NewTraceCode,
    int? NewRemain);

/// <summary>
/// 乐观锁冲突项：附带服务端当前行，供放弃加载 / 强制覆盖
/// </summary>
public sealed record StockRowEditConflict(
    string MatchTraceCode,
    string? NewTraceCode,
    int? NewRemain,
    TracePoolStockRowDto? Current);

public sealed record StockRowEditSaved(
    string MatchTraceCode,
    long NewVersion,
    string? NewTraceCode,
    int? NewRemain);

public sealed record StockRowEditBatchResult(
    int SavedCount,
    int FailedCount,
    string? LastError,
    IReadOnlyList<StockRowEditSaved> Saved,
    IReadOnlyList<StockRowEditConflict> Conflicts);

public sealed record StockReassignContext(
    string TargetDrugId,
    string TargetSpec,
    int TargetQty,
    string Reason,
    string OperatorName,
    string Source);
