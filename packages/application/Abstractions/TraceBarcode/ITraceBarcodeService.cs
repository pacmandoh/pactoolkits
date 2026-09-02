using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>从库存取码时写入预览审计，并记录手动预览与导出结果</summary>
public interface ITraceBarcodeService
{
    event Action? AuditCleared;

    Task<TraceBarcodePickResult> PickAsync(TraceBarcodePickRequest request, CancellationToken ct);

    Task<int> AuditAsync(TraceBarcodeAuditRequest request, CancellationToken ct);

    Task<int> ClearAuditLogAsync(CancellationToken ct, Guid? commandId = null);
}
