using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

/// <summary>
/// 扫码入库业务入口
/// </summary>
public interface IScanCodeService
{
    Task<ScanCodeSubmitResult> SubmitAsync(ScanCodeSubmitRequest request, CancellationToken ct);

    Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct);
}
