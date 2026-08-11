using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 扫码入库追溯码写入与已存在码查询
/// </summary>
public interface IScanCodeRepo
{
    Task<ScanCodeInsertResult> InsertTraceCodesAsync(
        string drugId,
        string spec,
        int qty,
        IReadOnlyList<string> traceCodes,
        CancellationToken ct);

    Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(
        IReadOnlyList<string> traceCodes,
        CancellationToken ct);
}
