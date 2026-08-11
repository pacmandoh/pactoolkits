namespace PacToolkits.Application.DTOs;

/// <summary>
/// 追溯码明细分析结果（含池内重复等）
/// </summary>
public sealed record TraceCodeDetailedAnalysis(
    int Total,
    int Invalid,
    int ScanDuplicate,
    int PoolDuplicate,
    int Valid,
    IReadOnlyList<string> ValidUniqueCodes);

public sealed record TraceCodeAnalysisResult(
    TraceCodeDetailedAnalysis Detailed,
    TraceCodeLineKind[] LineKinds,
    IReadOnlyList<string> PoolCheckCandidates);

public sealed record ScanCodeInsertResult(int RequestedCount, int InsertedCount, int SkippedCount);

/// <summary>
/// 扫码提交分析计数
///
/// Duplicate = 本次输入内重复；PoolDuplicate = 已在追溯池中；
/// Total = Invalid + Duplicate + PoolDuplicate + ValidUniqueCodes.Count
/// </summary>
public sealed record CodeAnalysis(
    int Total,
    int Invalid,
    int Duplicate,
    int PoolDuplicate,
    IReadOnlyList<string> ValidUniqueCodes);

public sealed record TraceCodeValidationRule(int RequiredLength, string Pattern);

public sealed record ScanCodeSubmitRequest(
    string DrugId,
    string Spec,
    IReadOnlyList<string> ValidUniqueCodes,
    CodeAnalysis Analysis,
    string ClientRaw,
    string Source = "manual");

/// <summary>
/// 扫码提交结果（含录入流水字段）
/// </summary>
public sealed record ScanCodeSubmitResult(
    bool DrugFound,
    ScanCodeInsertResult Insert,
    int QtyPerTrace,
    string EntryResult,
    string EntryMessage);
