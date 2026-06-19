using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

public interface IScanCodeService
{
    Task<ScanCodeSubmitResult> SubmitAsync(ScanCodeSubmitRequest request, CancellationToken ct);

    Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct);
}

public sealed class ScanCodeService : IScanCodeService
{
    private readonly IDrugIndexRepo _drugIndexRepo;
    private readonly IScanCodeRepo _scanCodeRepo;
    private readonly ITraceEntryLogService _traceEntryLog;

    public ScanCodeService(
        IDrugIndexRepo drugIndexRepo,
        IScanCodeRepo scanCodeRepo,
        ITraceEntryLogService traceEntryLog)
    {
        _drugIndexRepo = drugIndexRepo ?? throw new ArgumentNullException(nameof(drugIndexRepo));
        _scanCodeRepo = scanCodeRepo ?? throw new ArgumentNullException(nameof(scanCodeRepo));
        _traceEntryLog = traceEntryLog ?? throw new ArgumentNullException(nameof(traceEntryLog));
    }

    public async Task<ScanCodeSubmitResult> SubmitAsync(ScanCodeSubmitRequest request, CancellationToken ct)
    {
        var dto = await _drugIndexRepo.GetByKeyAsync(request.DrugId, request.Spec, ct).ConfigureAwait(false);
        if (dto is null)
        {
            return new ScanCodeSubmitResult(
                DrugFound: false,
                Insert: new ScanCodeInsertResult(0, 0, 0),
                QtyPerTrace: 0,
                EntryResult: "failed",
                EntryMessage: "drug/spec not found");
        }

        var result = await _scanCodeRepo
            .InsertTraceCodesAsync(request.DrugId, request.Spec, dto.Qty, request.ValidUniqueCodes, ct)
            .ConfigureAwait(false);

        var failedCount = Math.Max(0, request.Analysis.Total - result.InsertedCount);
        var entryResult = result.InsertedCount == 0
            ? "failed"
            : failedCount > 0 ? "partial" : "success";
        var entryMessage =
            $"{request.Source} input={request.Analysis.Total}, valid={request.ValidUniqueCodes.Count}, duplicate={request.Analysis.Duplicate}, invalid={request.Analysis.Invalid}, inserted={result.InsertedCount}, skipped={result.SkippedCount}";

        try
        {
            await _traceEntryLog.WriteAsync(new TraceEntryLogDto(
                EntryAt: DateTimeOffset.Now,
                DrugId: request.DrugId,
                Spec: request.Spec,
                EntryCount: request.Analysis.Total,
                QtyPerTrace: dto.Qty,
                TotalAvailableQty: result.InsertedCount * dto.Qty,
                FailedCount: failedCount,
                Result: entryResult,
                TxnId: null,
                Client: request.ClientRaw,
                Source: request.Source,
                Message: entryMessage
            ), ct).ConfigureAwait(false);
        }
        catch
        {
            entryMessage = $"insert ok but log failed: {entryMessage}";
        }

        return new ScanCodeSubmitResult(
            DrugFound: true,
            Insert: result,
            QtyPerTrace: dto.Qty,
            EntryResult: entryResult,
            EntryMessage: entryMessage);
    }

    public Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct)
        => _scanCodeRepo.FindExistingTraceCodesAsync(traceCodes, ct);
}
