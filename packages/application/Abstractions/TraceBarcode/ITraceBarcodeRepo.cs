using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>库存取码与预览审计必须在同一事务内完成</summary>
public interface ITraceBarcodeRepo
{
    Task<TraceBarcodePickGroup> PickAsync(
        Guid batchId,
        string operatorName,
        string drugId,
        string spec,
        int count,
        int? excludeRecentDays,
        IReadOnlyList<string> excludeTraceCodes,
        CancellationToken ct);

    Task<int> InsertAuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct);

    Task<int> ClearAuditLogAsync(CancellationToken ct);
}
