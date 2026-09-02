namespace PacToolkits.Application.DTOs;

public sealed record TraceBarcodePickedRow(
    string TraceCode,
    string DrugId,
    string Spec,
    int Qty,
    int Remain,
    DateTimeOffset? InDate);

public sealed record TraceBarcodePickGroup(
    string DrugId,
    string Spec,
    int Requested,
    int Picked,
    int Gap,
    IReadOnlyList<TraceBarcodePickedRow> Codes);

public sealed record TraceBarcodePickItem(string DrugId, string Spec, int Count);

public sealed record TraceBarcodePickRequest(
    Guid BatchId,
    string OperatorName,
    IReadOnlyList<TraceBarcodePickItem> Items,
    int? ExcludeRecentDays,
    IReadOnlyList<string>? ExcludeTraceCodes);

public sealed record TraceBarcodePickResult(IReadOnlyList<TraceBarcodePickGroup> Groups);

public sealed record TraceBarcodeAuditItem(
    string TraceCode,
    string? DrugId,
    string? Spec,
    string? FileName,
    bool Success = true,
    string? Error = null);

public sealed record TraceBarcodeAuditRequest(
    Guid BatchId,
    string Action,
    string OperatorName,
    IReadOnlyList<TraceBarcodeAuditItem> Items,
    Guid? CommandId = null);

public sealed record TraceBarcodeLabelInput(
    string TraceCode,
    string? DrugId,
    string? Spec,
    int? Qty,
    int? Remain,
    DateTimeOffset? InDate);
