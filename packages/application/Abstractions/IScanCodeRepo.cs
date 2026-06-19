using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Abstractions;

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
