namespace PacToolkits.Application.DTOs;

public enum TraceCodeLineStatus
{
    Blank,
    Valid,
    ScanDuplicate,
    PoolDuplicate,
    Invalid
}

public sealed record TraceCodeLineAnalysis(string Raw, string Code, TraceCodeLineStatus Status);

public sealed record TraceCodeDetailedAnalysis(
    int Total,
    int Invalid,
    int ScanDuplicate,
    int PoolDuplicate,
    int Valid,
    IReadOnlyList<string> ValidUniqueCodes,
    IReadOnlyList<TraceCodeLineAnalysis> Lines);

public sealed record ScanCodeInsertResult(int RequestedCount, int InsertedCount, int SkippedCount);

public sealed record CodeAnalysis(
    int Total,
    int Invalid,
    int Duplicate,
    IReadOnlyList<string> ValidUniqueCodes);
