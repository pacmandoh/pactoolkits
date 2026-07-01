namespace PacToolkits.Application.DTOs;

public sealed record StockCellEditRequest(
    string MatchTraceCode,
    string ColumnHeader,
    string? NewValue);

public sealed record StockCellEditBatchResult(
    int SavedCount,
    int FailedCount,
    string? LastError);

public sealed record StockReassignContext(
    string TargetDrugId,
    string TargetSpec,
    int TargetQty,
    string Reason,
    string OperatorName,
    string Source);
