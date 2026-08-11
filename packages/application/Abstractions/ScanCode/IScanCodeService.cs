using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 定义追溯码扫描入库的业务用例契约
/// </summary>
public interface IScanCodeService
{
    Task<ScanCodeSubmitResult> SubmitAsync(ScanCodeSubmitRequest request, CancellationToken ct);

    Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct);
}
