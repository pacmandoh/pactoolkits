namespace PacToolkits.Application.DTOs;

public sealed record InventoryStockEditRequest(
    IReadOnlyList<StockRowEditRequest> Edits,
    TraceCodeValidationRule TraceCodeRule);

/// <summary>按追溯码删库存</summary>
public sealed record InventoryStockDeleteRequest(IReadOnlyList<string> TraceCodes);

public sealed record InventoryStockDeleteResponse(int DeletedCount);

public sealed record InventoryTargetExistsResponse(bool Exists);

public sealed record InventoryReassignPreviewRequest(
    string Keyword,
    string TargetDrugId,
    string TargetSpec,
    int TargetQty,
    int SampleLimit);

/// <summary>库存改派提交；keyword 与 traceCodes 二选一</summary>
public sealed record InventoryReassignCommitRequest(
    string? Keyword,
    IReadOnlyList<string>? TraceCodes,
    StockReassignContext Context);
