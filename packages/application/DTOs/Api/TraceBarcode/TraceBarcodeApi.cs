namespace PacToolkits.Application.DTOs;

public sealed record TraceBarcodePickApiRequest(
    Guid BatchId,
    string OperatorName,
    IReadOnlyList<TraceBarcodePickItem> Items,
    int? ExcludeRecentDays,
    IReadOnlyList<string>? ExcludeTraceCodes);

public sealed record TraceBarcodeAuditApiRequest(
    Guid BatchId,
    string Action,
    string OperatorName,
    IReadOnlyList<TraceBarcodeAuditItem> Items);

public sealed record TraceBarcodeAuditApiResponse(int Inserted);

public sealed record TraceBarcodeClearAuditApiResponse(int Deleted);
