using PacToolkits.Application.Abstractions;
using PacToolkits.Application.DTOs;

namespace PacToolkits.Application.Services;

/// <summary>
/// 扫码入库：校验药品索引、写追溯池并记录入流水
/// </summary>
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
        ArgumentNullException.ThrowIfNull(request);
        EnsureAnalysisMatchesCodes(request);

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

        // 写库只认顶层 ValidUniqueCodes；Analysis 仅作流水计数且须与之对齐
        var codes = request.ValidUniqueCodes;
        var analysis = request.Analysis;
        var result = await _scanCodeRepo
            .InsertTraceCodesAsync(request.DrugId, request.Spec, dto.Qty, codes, ct)
            .ConfigureAwait(false);

        // 池内/扫码重复只是输入过滤；失败只计无效与未写入的有效码
        var insertMiss = Math.Max(0, codes.Count - result.InsertedCount);
        var failedCount = analysis.Invalid + insertMiss;
        var entryResult = result.InsertedCount == 0
            ? failedCount > 0 ? "failed" : "success"
            : failedCount > 0 ? "partial" : "success";
        var entryMessage =
            $"{request.Source} input={analysis.Total}, valid={codes.Count}, duplicate={analysis.Duplicate}, poolDuplicate={analysis.PoolDuplicate}, invalid={analysis.Invalid}, inserted={result.InsertedCount}, skipped={result.SkippedCount}";

        await _traceEntryLog.WriteAsync(new TraceEntryLogDto(
            EntryAt: DateTimeOffset.Now,
            DrugId: request.DrugId,
            Spec: request.Spec,
            EntryCount: analysis.Total,
            QtyPerTrace: dto.Qty,
            TotalAvailableQty: result.InsertedCount * dto.Qty,
            FailedCount: failedCount,
            Result: entryResult,
            TxnId: null,
            Client: request.ClientRaw,
            Source: request.Source,
            Message: entryMessage
        ), ct).ConfigureAwait(false);

        return new ScanCodeSubmitResult(
            DrugFound: true,
            Insert: result,
            QtyPerTrace: dto.Qty,
            EntryResult: entryResult,
            EntryMessage: entryMessage);
    }

    /// <summary>
    /// ValidUniqueCodes 与 Analysis 必须同一套数据，否则流水计数与落库会互相矛盾
    /// </summary>
    private static void EnsureAnalysisMatchesCodes(ScanCodeSubmitRequest request)
    {
        var codes = request.ValidUniqueCodes
                    ?? throw new ArgumentException("ValidUniqueCodes is required", nameof(request));
        var analysis = request.Analysis
                       ?? throw new ArgumentException("Analysis is required", nameof(request));
        var analysisCodes = analysis.ValidUniqueCodes
                            ?? throw new ArgumentException("Analysis.ValidUniqueCodes is required", nameof(request));

        if (analysis.Total < 0
            || analysis.Invalid < 0
            || analysis.Duplicate < 0
            || analysis.PoolDuplicate < 0)
        {
            throw new ArgumentException("Analysis counts must be non-negative", nameof(request));
        }

        // ValidUniqueCodes 已排除池内重复；Total 须含 PoolDuplicate
        if (analysis.Total
            != analysis.Invalid + analysis.Duplicate + analysis.PoolDuplicate + codes.Count)
        {
            throw new ArgumentException(
                "Analysis.Total must equal Invalid + Duplicate + PoolDuplicate + ValidUniqueCodes.Count",
                nameof(request));
        }

        if (analysisCodes.Count != codes.Count)
        {
            throw new ArgumentException(
                "Analysis.ValidUniqueCodes must match ValidUniqueCodes",
                nameof(request));
        }

        for (var i = 0; i < codes.Count; i++)
        {
            if (!string.Equals(codes[i], analysisCodes[i], StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Analysis.ValidUniqueCodes must match ValidUniqueCodes",
                    nameof(request));
            }
        }
    }

    public Task<IReadOnlyList<string>> FindExistingTraceCodesAsync(IReadOnlyList<string> traceCodes, CancellationToken ct)
        => _scanCodeRepo.FindExistingTraceCodesAsync(traceCodes, ct);
}
